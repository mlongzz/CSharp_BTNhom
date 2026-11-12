using Bookstore.Domain;

namespace Bookstore.Models;

public record CartLine(Book Book, int Quantity)
{
    public long Subtotal => (long)Book.Price * Quantity;
}

public record CartViewModel(IReadOnlyList<CartLine> Lines, string? Address, IReadOnlyList<ShippingAddress> Addresses)
{
    public long? AddressId { get; init; }
    public string? Voucher { get; init; }
    public string? Method { get; init; }

    public IReadOnlyList<string> StockNotices { get; init; } = [];

    public long Total => Lines.Sum(line => line.Subtotal);
}

public record CheckoutViewModel(OrderQuote Quote, string Address, string Method, string Ticket);

public record CheckoutPaymentViewModel(long Total, string Ticket);
