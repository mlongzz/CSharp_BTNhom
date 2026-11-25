using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = "Customer")]
public class OrdersController(OrderService orders, ICustomerAccounts accounts, IDataProtectionProvider protection,
    TimeProvider clock, ILogger<OrdersController> logger) : Controller
{
    private readonly IDataProtector checkout = protection.CreateProtector("Bookstore.Checkout.v1");
    private string Customer => User.Identity!.Name!;
    private string CartKey => $"cart:{Customer}";

    [HttpGet]
    public IActionResult Index() => View(orders.CustomerOrders(Customer));

    [HttpGet]
    public IActionResult Cart() => View(BuildCart(ReadCart(), null));

    [HttpGet]
    public IActionResult Result(long id)
    {
        var order = orders.CustomerOrders(Customer).SingleOrDefault(item => item.Id == id);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost]
    public IActionResult Add(int bookId, int quantity, bool buyNow = false)
    {
        var book = orders.Books().FirstOrDefault(book => book.Id == bookId);
        var cart = ReadCart();
        if (!ModelState.IsValid || book is null || quantity <= 0 || book.Stock == 0)
        {
            TempData["Error"] = "Sách hoặc số lượng đã chọn không hợp lệ. Vui lòng kiểm tra tồn kho.";
            return RedirectToAction("Index", "Home");
        }

        var requested = (long)cart.GetValueOrDefault(bookId) + quantity;
        cart[bookId] = (int)Math.Min(requested, book.Stock);
        SaveCart(cart);
        if (requested > book.Stock)
            TempData["Error"] = $"Giỏ đã điều chỉnh về số lượng còn lại: {book.Stock} cuốn.";
        else
            TempData["Success"] = $"Đã thêm {quantity} cuốn vào giỏ.";
        return RedirectToAction(nameof(Cart), null, null, buyNow ? "checkout" : null);
    }

    [HttpPost]
    public IActionResult UpdateCart([Bind(Prefix = "quantities")] Dictionary<int, int>? quantities)
    {
        var cart = ReadCart();
        var stock = orders.Books().ToDictionary(book => book.Id, book => book.Stock);
        if (!ModelState.IsValid || quantities is null || quantities.Any(item => !cart.ContainsKey(item.Key) ||
            item.Value < 0))
        {
            TempData["Error"] = "Số lượng cập nhật không hợp lệ. Vui lòng kiểm tra tồn kho.";
            return RedirectToAction(nameof(Cart));
        }

        foreach (var (bookId, quantity) in quantities)
        {
            var available = stock.GetValueOrDefault(bookId);
            if (quantity > available)
                TempData["Error"] = "Đã điều chỉnh các số lượng vượt tồn kho.";
            if (quantity == 0 || available == 0)
                cart.Remove(bookId);
            else
                cart[bookId] = Math.Min(quantity, available);
        }
        SaveCart(cart);
        return RedirectToAction(nameof(Cart));
    }

    [HttpPost]
    public IActionResult Review(string? address, long? addressId, string? voucher, string method)
    {
        var selected = ReadCart();

        CartViewModel CurrentCart() => BuildCart(selected, address) with
        {
            AddressId = addressId,
            Voucher = voucher,
            Method = method
        };

        try
        {
            if (!ModelState.IsValid || method is not ("Cod" or "Online"))
                throw new OrderRuleException("Chọn phương thức thanh toán hợp lệ.");
            var shippingAddress = string.IsNullOrWhiteSpace(address) && addressId is long savedId
                ? accounts.Address(Customer, savedId)?.Formatted
                    ?? throw new OrderRuleException("Địa chỉ đã lưu không còn khả dụng.")
                : address;

            var currentCart = CurrentCart();
            if (currentCart.StockNotices.Count > 0)
                return View(nameof(Cart), currentCart);

            var quote = orders.Quote(shippingAddress, selected, voucher);
            var draft = new CheckoutDraft(Customer, shippingAddress!, selected, voucher, method,
                quote.Total, Guid.NewGuid().ToString("N"), clock.GetUtcNow().AddMinutes(15));
            var ticket = checkout.Protect(JsonSerializer.Serialize(draft));
            return View(new CheckoutViewModel(quote, shippingAddress!, method, ticket));
        }
        catch (OrderRuleException exception)
        {
            ModelState.AddModelError("", exception.Message);
            return View(nameof(Cart), CurrentCart());
        }
    }

    [HttpPost]
    public IActionResult Place(string ticket) => Submit(ticket, "Cod", "Success");

    [HttpPost]
    public IActionResult Payment(string ticket)
    {
        var draft = ReadDraft(ticket);
        if (draft is null || draft.Method != "Online")
        {
            TempData["Error"] = "Phiên thanh toán không hợp lệ hoặc đã hết hạn.";
            return RedirectToAction(nameof(Cart));
        }
        return View(new CheckoutPaymentViewModel(draft.Total, ticket));
    }

    [HttpPost]
    public IActionResult PaymentResult(string ticket, string result) => Submit(ticket, "Online", result);

    private IActionResult Submit(string ticket, string method, string result)
    {
        var draft = ReadDraft(ticket);
        if (draft is null || draft.Method != method)
        {
            TempData["Error"] = "Phiên thanh toán không hợp lệ hoặc đã hết hạn.";
            return RedirectToAction(nameof(Cart));
        }

        // A retry can arrive after checkout has cleared the old cart.
        if (orders.FindPlacedOrder(Customer, draft.Reference) is long previous)
            return RedirectToAction(nameof(Result), new { id = previous });

        var selected = ReadCart();
        var cartChanged = selected.Count != draft.Quantities.Count ||
            draft.Quantities.Any(item => selected.GetValueOrDefault(item.Key) != item.Value);
        if (result != "Success" || cartChanged)
        {
            if (method == "Online" && result is "Failure" or "Expired")
                orders.RejectPayment(draft.Reference);
            TempData["Error"] = result == "Success"
                ? "Giỏ hàng đã thay đổi. Vui lòng kiểm tra lại."
                : "Thanh toán thất bại hoặc đã hết hạn. Giỏ hàng được giữ lại.";
            return RedirectToAction(nameof(Cart));
        }

        return PlaceOrder(draft.Address, selected, draft.Voucher,
            draft.Reference, draft.Total, method == "Online");
    }

    private CheckoutDraft? ReadDraft(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            return null;
        try
        {
            var draft = JsonSerializer.Deserialize<CheckoutDraft>(checkout.Unprotect(ticket));
            return draft is not null && draft.Customer == Customer && draft.ExpiresAt > clock.GetUtcNow()
                ? draft : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    [HttpPost("/Home/Place")]
    public IActionResult PlaceLegacy(string? address, [Bind(Prefix = "quantities")] Dictionary<int, int>? quantities) =>
        PlaceOrder(address, quantities?.Where(item => item.Value != 0).ToDictionary() ?? [], reconcileSession: false);

    private IActionResult PlaceOrder(string? address, Dictionary<int, int> selected, string? voucher = null,
        string? paymentReference = null, long? expectedTotal = null, bool online = false, bool reconcileSession = true)
    {
        try
        {
            if (!ModelState.IsValid)
                throw new OrderRuleException("Thông tin đặt hàng không hợp lệ.");

            var id = orders.Place(Customer, address, selected, voucher, paymentReference, expectedTotal, online);
            HttpContext.Session.Remove(CartKey);
            logger.LogInformation("Order {OrderId} placed by {Customer}", id, Customer);
            TempData["Success"] = $"Đã đặt đơn #{id}.";
            return RedirectToAction(nameof(Result), new { id });
        }
        catch (OrderRuleException exception)
        {
            ModelState.AddModelError("", exception.Message);
            return View(nameof(Cart), BuildCart(selected, address, reconcileSession));
        }
    }

    private sealed record CheckoutDraft(string Customer, string Address, Dictionary<int, int> Quantities,
        string? Voucher, string Method, long Total, string Reference, DateTimeOffset ExpiresAt);

    [HttpPost]
    public IActionResult Cancel(long id)
    {
        try
        {
            orders.Cancel(id, Customer);
            logger.LogInformation("Order {OrderId} cancelled by its customer", id);
            TempData["Success"] = $"Đã hủy đơn #{id}.";
        }
        catch (OrderRuleException exception)
        {
            logger.LogWarning("Cancellation rejected for order {OrderId}", id);
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    private Dictionary<int, int> ReadCart()
    {
        var json = HttpContext.Session.GetString(CartKey);
        return json is null ? [] : JsonSerializer.Deserialize<Dictionary<int, int>>(json) ?? [];
    }

    private void SaveCart(Dictionary<int, int> cart) =>
        HttpContext.Session.SetString(CartKey, JsonSerializer.Serialize(cart));

    private CartViewModel BuildCart(Dictionary<int, int> cart, string? address, bool reconcileSession = true)
    {
        var books = orders.Books().ToDictionary(book => book.Id);
        var lines = new List<CartLine>();
        var notices = new List<string>();
        foreach (var (id, quantity) in cart)
        {
            if (!books.TryGetValue(id, out var book))
            {
                notices.Add("Đã bỏ một đầu sách ngừng kinh doanh khỏi giỏ.");
                continue;
            }
            if (book.Stock == 0)
            {
                notices.Add($"{book.Title} đã hết hàng và được bỏ khỏi giỏ.");
                continue;
            }
            if (quantity > book.Stock)
                notices.Add($"{book.Title} chỉ còn {book.Stock} cuốn. Số lượng trong giỏ đã giảm từ {quantity} xuống {book.Stock}.");

            lines.Add(new CartLine(book, Math.Min(quantity, book.Stock)));
        }

        if (reconcileSession && notices.Count > 0)
            SaveCart(lines.ToDictionary(line => line.Book.Id, line => line.Quantity));

        var addresses = accounts.Addresses(Customer);
        return new CartViewModel(lines, address, addresses)
        {
            StockNotices = notices,
            AddressId = addresses.FirstOrDefault(saved => saved.IsDefault)?.Id
        };
    }
}
