using Bookstore.Domain;

namespace Bookstore.Application;

public sealed class OrderService(IOrderRepository repository, TimeProvider clock)
{
    public IReadOnlyList<Book> Books() => repository.Books();
    public IReadOnlyList<Order> CustomerOrders(string customer) => repository.Orders(customer);
    public IReadOnlyList<Order> AllOrders() => repository.Orders(null);
    public IReadOnlyList<ReturnRequest> Returns(string? customer = null) => repository.Returns(customer);

    public OrderQuote Quote(string? address, IReadOnlyDictionary<int, int> quantities, string? voucher)
    {
        OrderRules.ValidatePlacement(address, quantities);
        using var transaction = repository.BeginTransaction();
        return Price(transaction, SelectLines(transaction, quantities), voucher, false);
    }

    public void RejectPayment(string reference)
    {
        using var transaction = repository.BeginTransaction();
        transaction.RejectPayment(reference);
        transaction.Commit();
    }

    public long Place(string customer, string? address, IReadOnlyDictionary<int, int> quantities,
        string? voucher = null, string? paymentReference = null, long? expectedTotal = null, bool online = false)
    {
        var shippingAddress = OrderRules.ValidatePlacement(address, quantities);
        using var transaction = repository.BeginTransaction();
        if (paymentReference is not null && transaction.FindPayment(customer, paymentReference) is long previous)
            return previous;
        if (paymentReference is not null && transaction.PaymentRejected(paymentReference))
            throw new OrderRuleException("Phiên thanh toán đã kết thúc. Vui lòng tạo lượt thanh toán mới.");

        var lines = SelectLines(transaction, quantities);
        var quote = Price(transaction, lines, voucher, true);
        if (expectedTotal is long expected && expected != quote.Total)
            throw new OrderRuleException("Tổng tiền đã thay đổi. Vui lòng kiểm tra giỏ và thanh toán lại.");

        var now = clock.GetUtcNow();
        var orderId = transaction.InsertOrder(customer, shippingAddress, quote, lines, now, paymentReference, online);
        transaction.AppendEvent(orderId, new(OrderAction.Placed, customer, now));
        transaction.Notify(customer, $"Xác nhận đặt đơn #{orderId}", $"Đơn #{orderId} đang chờ xác nhận.");
        transaction.Commit();
        return orderId;
    }

    private static List<OrderLine> SelectLines(IOrderTransaction transaction, IReadOnlyDictionary<int, int> quantities)
    {
        var lines = new List<OrderLine>(quantities.Count);
        foreach (var (bookId, quantity) in quantities)
        {
            var book = transaction.FindBook(bookId)
                ?? throw new OrderRuleException("Không tìm thấy sách đang kinh doanh đã chọn.");
            lines.Add(OrderRules.Select(book, quantity));
        }
        return lines;
    }

    private static OrderQuote Price(IOrderTransaction transaction, List<OrderLine> lines, string? voucher, bool redeem)
    {
        var subtotal = OrderRules.Total(lines);
        const long shippingFee = 15_000;
        var discount = string.IsNullOrWhiteSpace(voucher)
            ? 0 : transaction.VoucherDiscount(voucher.Trim().ToUpperInvariant(), subtotal, redeem);
        return new(subtotal, discount, shippingFee);
    }

    public void Confirm(long id, string staff)
    {
        using var transaction = repository.BeginTransaction();
        var order = transaction.FindOrder(id) ?? throw OrderNotFound();
        OrderRules.EnsureCanConfirm(order);
        var now = clock.GetUtcNow();
        foreach (var line in order.Lines)
        {
            if (!transaction.TryDeductStock(line.BookId, line.Quantity))
                throw new OrderRuleException($"Sách {line.Title} không đủ tồn kho để xác nhận.");
            transaction.StockMovement(line.BookId, -line.Quantity, "Sale", id, staff, now);
        }
        transaction.UpdateStatus(id, OrderStatus.Confirmed, now);
        transaction.AppendEvent(id, new(OrderAction.Confirmed, staff, now));
        transaction.Notify(order.Customer, $"Đơn #{id} đã được xác nhận", $"Đơn #{id} đang được chuẩn bị.");
        transaction.Commit();
    }

    public void Cancel(long id, string customer)
    {
        using var transaction = repository.BeginTransaction();
        var order = transaction.FindOrder(id);
        if (order is null || order.Customer != customer)
            throw OrderNotFound();
        var now = clock.GetUtcNow();
        if (OrderRules.ShouldRestoreStockOnCancellation(order))
        {
            foreach (var line in order.Lines)
            {
                transaction.RestoreStock(line.BookId, line.Quantity);
                transaction.StockMovement(line.BookId, line.Quantity, "Cancellation", id, customer, now);
            }
        }
        transaction.UpdateStatus(id, OrderStatus.Cancelled, now);
        transaction.AppendEvent(id, new(OrderAction.Cancelled, customer, now));
        transaction.Commit();
    }

    public void Fulfill(long id, string staff, OrderAction action, string? carrier, string? tracking, string? reason)
    {
        using var transaction = repository.BeginTransaction();
        var order = transaction.FindOrder(id) ?? throw OrderNotFound();
        var next = (order.Status, action) switch
        {
            (OrderStatus.Pending, OrderAction.Rejected) when !string.IsNullOrWhiteSpace(reason) && reason.Length <= 500
                => OrderStatus.Cancelled,
            (OrderStatus.Confirmed, OrderAction.Packing) => OrderStatus.Packing,
            (OrderStatus.Packing, OrderAction.Shipped) when !string.IsNullOrWhiteSpace(carrier) && carrier.Length <= 100 &&
                !string.IsNullOrWhiteSpace(tracking) && tracking.Length <= 100 => OrderStatus.Shipped,
            (OrderStatus.Shipped, OrderAction.Completed) => OrderStatus.Completed,
            _ => throw new OrderRuleException("Trạng thái hoặc thông tin xử lý đơn không hợp lệ.")
        };
        var now = clock.GetUtcNow();
        transaction.UpdateStatus(id, next, now, action == OrderAction.Shipped ? carrier!.Trim() : null,
            action == OrderAction.Shipped ? tracking!.Trim() : null, action == OrderAction.Rejected ? reason!.Trim() : null);
        transaction.AppendEvent(id, new(action, staff, now));
        transaction.Commit();
    }

    public void RequestReturn(long orderId, int bookId, string customer, string? reason, string outcome)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || outcome is not ("Refund" or "Exchange"))
            throw new OrderRuleException("Nhập lý do đổi trả và chọn hình thức hợp lệ.");
        using var transaction = repository.BeginTransaction();
        var order = transaction.FindOrder(orderId);
        var now = clock.GetUtcNow();
        if (order is null || order.Customer != customer || order.Status != OrderStatus.Completed ||
            order.CompletedAt is null || now - order.CompletedAt > TimeSpan.FromDays(7))
            throw new OrderRuleException("Chỉ yêu cầu đổi trả trong 7 ngày sau khi nhận hàng.");
        var line = order.Lines.SingleOrDefault(item => item.BookId == bookId)
            ?? throw new OrderRuleException("Sách không thuộc đơn này.");
        transaction.InsertReturn(orderId, bookId, line.Quantity, reason.Trim(), outcome, now);
        transaction.Commit();
    }

    public void ProcessReturn(long id, string staff, string staffRole, string decision)
    {
        using var transaction = repository.BeginTransaction();
        var request = transaction.FindReturn(id) ?? throw new OrderRuleException("Không tìm thấy yêu cầu đổi trả.");
        var now = clock.GetUtcNow();
        var status = (staffRole, request.Status, decision) switch
        {
            ("Sales", "Pending", "Approve") => "Approved",
            ("Sales", "Pending", "Reject") => "Rejected",
            ("Warehouse", "Approved", "Reject") => "Rejected",
            ("Warehouse", "Approved", "Receive") => "Completed",
            _ => throw new OrderRuleException("Không thể thực hiện thao tác đổi trả này.")
        };
        if (status == "Completed")
        {
            transaction.RestoreStock(request.BookId, request.Quantity);
            transaction.StockMovement(request.BookId, request.Quantity, "Return", id, staff, now);
            if (request.Outcome == "Exchange")
            {
                if (!transaction.TryDeductStock(request.BookId, request.Quantity))
                    throw new OrderRuleException("Không đủ sách để đổi.");
                transaction.StockMovement(request.BookId, -request.Quantity, "Exchange", id, staff, now);
            }
        }
        var refund = 0L;
        if (status == "Completed" && request.Outcome == "Refund")
        {
            var order = transaction.FindOrder(request.OrderId) ?? throw OrderNotFound();
            var line = order.Lines.Single(item => item.BookId == request.BookId);
            var gross = (long)line.Price * request.Quantity;
            var subtotal = OrderRules.Total(order.Lines);
            var previous = OrderRules.Total(order.Lines.Where(item => item.BookId < request.BookId));
            var discountThroughLine = (long)((previous + gross) * (decimal)order.Discount / subtotal);
            var discountBeforeLine = (long)(previous * (decimal)order.Discount / subtotal);
            refund = gross - (discountThroughLine - discountBeforeLine);
        }
        transaction.UpdateReturn(id, status, now, refund);
        transaction.Commit();
    }

    private static OrderRuleException OrderNotFound() => new("Không tìm thấy đơn hàng.");
}

