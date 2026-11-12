using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = "Sales")]
public class AdminOrdersController(OrderService orders, ILogger<AdminOrdersController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index(OrderStatus? status, string? customer, long? orderId, DateOnly? from, DateOnly? to)
    {
        ViewData["Status"] = status;
        var allOrders = orders.AllOrders();
        ViewData["PendingCount"] = allOrders.Count(order => order.Status == OrderStatus.Pending);
        ViewData["ConfirmedCount"] = allOrders.Count(order => order.Status == OrderStatus.Confirmed);
        ViewData["CancelledCount"] = allOrders.Count(order => order.Status == OrderStatus.Cancelled);
        ViewData["Customer"] = customer;
        ViewData["OrderId"] = orderId;
        ViewData["From"] = from?.ToString("yyyy-MM-dd");
        ViewData["To"] = to?.ToString("yyyy-MM-dd");
        if (from > to)
            ModelState.AddModelError(nameof(to), "Đến ngày phải bằng hoặc sau Từ ngày.");

        if (!ModelState.IsValid)
            return View(Array.Empty<Order>());

        return View(allOrders.Where(order => (status is null || order.Status == status) &&
            (orderId is null || order.Id == orderId) &&
            (string.IsNullOrWhiteSpace(customer) || order.Customer.Contains(customer, StringComparison.OrdinalIgnoreCase)) &&
            (from is null || LocalDate(order.CreatedAt) >= from) &&
            (to is null || LocalDate(order.CreatedAt) <= to)).ToList());
    }

    private static DateOnly? LocalDate(DateTimeOffset? time) =>
        time is null ? null : DateOnly.FromDateTime(time.Value.ToOffset(TimeSpan.FromHours(7)).DateTime);

    [HttpPost]
    public IActionResult Fulfill(long id, OrderAction action, string? carrier, string? tracking, string? reason)
    {
        try
        {
            if (!ModelState.IsValid)
                throw new OrderRuleException("Thông tin xử lý đơn không hợp lệ.");
            orders.Fulfill(id, User.Identity!.Name!, action, carrier, tracking, reason);
            logger.LogInformation("Order {OrderId} updated: {Action}", id, action);
            TempData["Success"] = $"Đã cập nhật đơn #{id}.";
        }
        catch (OrderRuleException exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult Confirm(long id)
    {
        try
        {
            orders.Confirm(id, User.Identity!.Name!);
            logger.LogInformation("Order {OrderId} confirmed by {Staff}", id, User.Identity!.Name);
            TempData["Success"] = $"Đã xác nhận đơn #{id}.";
        }
        catch (OrderRuleException exception)
        {
            logger.LogWarning("Confirmation rejected for order {OrderId}", id);
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
