using Bookstore.Domain;

namespace Bookstore.Application;

public interface IOrderRepository
{
    IReadOnlyList<Book> Books();
    IReadOnlyList<Order> Orders(string? customer);
    IReadOnlyList<ReturnRequest> Returns(string? customer);
    IOrderTransaction BeginTransaction();
}

public interface IOrderTransaction : IDisposable
{
    Book? FindBook(int id);
    Order? FindOrder(long id);
    void RejectPayment(string reference);
    bool PaymentRejected(string reference);
    long? FindPayment(string customer, string reference);
    long VoucherDiscount(string code, long subtotal, bool redeem);
    long InsertOrder(string customer, string address, OrderQuote quote, IReadOnlyList<OrderLine> lines,
        DateTimeOffset created, string? paymentReference, bool online);
    void UpdateStatus(long id, OrderStatus status, DateTimeOffset at, string? carrier = null,
        string? tracking = null, string? rejection = null);
    bool TryDeductStock(int bookId, int quantity);
    void RestoreStock(int bookId, int quantity);
    void StockMovement(int bookId, int delta, string kind, long reference, string actor, DateTimeOffset at);
    void AppendEvent(long orderId, OrderEvent entry);
    void Notify(string customer, string subject, string message);
    void InsertReturn(long orderId, int bookId, int quantity, string reason, string outcome, DateTimeOffset at);
    ReturnRequest? FindReturn(long id);
    void UpdateReturn(long id, string status, DateTimeOffset at, long refundAmount);
    void Commit();
}
