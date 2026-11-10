namespace Bookstore.Domain;

public enum OrderStatus { Pending, Confirmed, Packing, Shipped, Completed, Cancelled }

public enum OrderAction { Placed, Confirmed, Packing, Shipped, Completed, Cancelled, Rejected }

public sealed record Book(int Id, string Title, int Price, int Stock);
public sealed record OrderLine(int BookId, string Title, int Quantity, int Price);
public sealed record OrderEvent(OrderAction Action, string Actor, DateTimeOffset OccurredAt);
public sealed record Order(long Id, string Customer, string Address, OrderStatus Status, long Total,
    IReadOnlyList<OrderLine> Lines, IReadOnlyList<OrderEvent> Events)
{
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Carrier { get; init; }
    public string? TrackingCode { get; init; }
    public string? RejectionReason { get; init; }
    public string PaymentMethod { get; init; } = "Cod";
    public string PaymentStatus { get; init; } = "Unpaid";
    public long Discount { get; init; }
    public long ShippingFee { get; init; }
}

public sealed record OrderQuote(long Subtotal, long Discount, long ShippingFee)
{
    public long Total => Subtotal - Discount + ShippingFee;
}

public sealed record ReturnRequest(long Id, long OrderId, int BookId, int Quantity, string Reason,
    string Status, string Outcome, string Customer, string Title, long RefundAmount);

public sealed class OrderRuleException(string message) : Exception(message);
