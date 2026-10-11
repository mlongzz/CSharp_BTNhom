using Bookstore.Domain;

namespace Bookstore.Models;

public sealed record WarehouseViewModel(IReadOnlyList<InventoryBook> Books, IReadOnlyList<Supplier> Suppliers,
    IReadOnlyList<PurchaseOrder> Purchases);

public sealed record InventoryViewModel(IReadOnlyList<InventoryBook> Books,
    IReadOnlyList<StockMovement> Movements, int? SelectedBook);
