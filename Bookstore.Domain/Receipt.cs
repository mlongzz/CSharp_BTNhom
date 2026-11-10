namespace Bookstore.Domain;

public enum PurchaseStatus { Pending, Approved, Received }

public sealed record Supplier(int Id, string Name, string Phone, string Email, string Address);

public sealed record PurchaseOrder(long Id, string Supplier, string CreatedBy, DateTimeOffset CreatedAt,
    PurchaseStatus Status, string? ApprovedBy, string? ReceivedBy, DateTimeOffset? ReceivedAt,
    string? Note, IReadOnlyList<PurchaseLine> Lines)
{
    public long Total => Lines.Sum(line => (long)line.Quantity * line.UnitCost);
}

public sealed record PurchaseLine(int BookId, string Title, int Quantity, int UnitCost, int? ReceivedQuantity);

public sealed record PurchaseInput(int Quantity, int UnitCost);

public sealed record InventoryBook(int Id, string Title, int Stock, int Minimum);

public sealed record StockMovement(string Title, int Delta, string Kind, long? Reference, string Actor,
    DateTimeOffset OccurredAt, string? Note);

public sealed class ReceivingRuleException(string message) : Exception(message);
