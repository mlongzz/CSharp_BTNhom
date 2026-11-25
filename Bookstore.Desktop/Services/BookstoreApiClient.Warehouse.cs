using Bookstore.Domain;

namespace Bookstore.Desktop.Services;

public sealed partial class BookstoreApiClient
{
    private const string WarehousePath = "api/admin/warehouse";

    public Task<List<Supplier>> GetSuppliersAsync() => GetAsync<List<Supplier>>($"{WarehousePath}/suppliers");

    public Task SaveSupplierAsync(Supplier supplier) => SendAsync(
        supplier.Id == 0 ? HttpMethod.Post : HttpMethod.Put,
        supplier.Id == 0 ? $"{WarehousePath}/suppliers" : $"{WarehousePath}/suppliers/{supplier.Id}", supplier);

    public Task DeleteSupplierAsync(int id) => SendAsync(HttpMethod.Delete, $"{WarehousePath}/suppliers/{id}");

    public Task<List<PurchaseOrder>> GetPurchasesAsync() => GetAsync<List<PurchaseOrder>>($"{WarehousePath}/purchases");

    public Task CreatePurchaseAsync(int supplierId, IReadOnlyDictionary<int, PurchaseInput> lines) =>
        SendAsync(HttpMethod.Post, $"{WarehousePath}/purchases", new { supplierId, lines });

    public Task ApprovePurchaseAsync(long id) => SendAsync(HttpMethod.Post, $"{WarehousePath}/purchases/{id}/approve");

    public Task ReceivePurchaseAsync(long id, IReadOnlyDictionary<int, int> quantities, string note) =>
        SendAsync(HttpMethod.Post, $"{WarehousePath}/purchases/{id}/receive", new { quantities, note });

    public Task<List<InventoryBook>> GetInventoryAsync() => GetAsync<List<InventoryBook>>($"{WarehousePath}/inventory");

    public Task<List<StockMovement>> GetStockMovementsAsync(int? bookId) => GetAsync<List<StockMovement>>(
        bookId is int id ? $"{WarehousePath}/movements?bookId={id}" : $"{WarehousePath}/movements");

    public Task SetStockMinimumAsync(int bookId, int minimum) =>
        SendAsync(HttpMethod.Put, $"{WarehousePath}/inventory/{bookId}/minimum", new { minimum });

    public Task ReconcileStockAsync(int bookId, int expectedStock, int count, string reason) =>
        SendAsync(HttpMethod.Post, $"{WarehousePath}/inventory/{bookId}/reconcile", new { expectedStock, count, reason });
}
