using System.Net.Mail;
using Bookstore.Domain;

namespace Bookstore.Application;

public interface IReceiptRepository
{
    IReadOnlyList<Supplier> Suppliers();
    void SaveSupplier(Supplier supplier);
    void DeleteSupplier(int id);
    IReadOnlyList<PurchaseOrder> Purchases();
    long Create(string staff, int supplierId, IReadOnlyDictionary<int, PurchaseInput> lines, DateTimeOffset now);
    void Approve(long id, string admin);
    void Receive(long id, string staff, IReadOnlyDictionary<int, int> quantities, string? note, DateTimeOffset now);
    IReadOnlyList<InventoryBook> Inventory();
    IReadOnlyList<StockMovement> Movements(int? bookId);
    void SetMinimum(int bookId, int minimum);
    void Reconcile(int bookId, int expectedStock, int count, string reason, string actor, DateTimeOffset now);
}

public sealed class ReceivingService(IReceiptRepository receipts, TimeProvider clock)
{
    public IReadOnlyList<Supplier> Suppliers() => receipts.Suppliers();
    public IReadOnlyList<PurchaseOrder> Purchases() => receipts.Purchases();
    public IReadOnlyList<InventoryBook> Inventory() => receipts.Inventory();
    public IReadOnlyList<StockMovement> Movements(int? bookId) => receipts.Movements(bookId);

    public void SaveSupplier(int id, string? name, string? phone, string? email, string? address)
    {
        name = name?.Trim() ?? "";
        phone = phone?.Trim() ?? "";
        email = email?.Trim() ?? "";
        address = address?.Trim() ?? "";
        if (name.Length is 0 or > 120 || address.Length > 255 || email.Length > 254)
            throw new ReceivingRuleException("Kiểm tra tên, email và địa chỉ nhà cung cấp.");
        if (phone.Length > 0 && (phone.Length != 10 || phone[0] != '0' || !phone.All(char.IsAsciiDigit)))
            throw new ReceivingRuleException("Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.");
        if (email.Length > 0 && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
            throw new ReceivingRuleException("Email nhà cung cấp không hợp lệ.");

        receipts.SaveSupplier(new(id, name, phone, email, address));
    }

    public void DeleteSupplier(int id) => receipts.DeleteSupplier(id);

    public long Create(string staff, int supplierId, IReadOnlyDictionary<int, PurchaseInput> lines)
    {
        if (supplierId <= 0 || lines.Count == 0 || lines.Values.Any(line => line.Quantity is <= 0 or > 1_000_000
            || line.UnitCost <= 0))
            throw new ReceivingRuleException("Chọn nhà cung cấp và nhập số lượng cùng giá nhập dương cho từng dòng.");

        return receipts.Create(staff, supplierId, lines, clock.GetUtcNow());
    }

    public void Approve(long id, string admin) => receipts.Approve(id, admin);

    public void Receive(long id, string staff, IReadOnlyDictionary<int, int> quantities, string? note)
    {
        note = note?.Trim();
        if (quantities.Count == 0 || quantities.Values.Any(quantity => quantity < 0)
            || quantities.Values.All(quantity => quantity == 0) || note?.Length > 500)
            throw new ReceivingRuleException("Nhập số lượng thực nhận hợp lệ và ghi chú tối đa 500 ký tự.");

        receipts.Receive(id, staff, quantities, note, clock.GetUtcNow());
    }

    public void SetMinimum(int bookId, int minimum)
    {
        if (minimum < 0)
            throw new ReceivingRuleException("Ngưỡng cảnh báo không được âm.");

        receipts.SetMinimum(bookId, minimum);
    }

    public void Reconcile(int bookId, int expectedStock, int count, string? reason, string actor)
    {
        reason = reason?.Trim();
        if (expectedStock < 0 || count < 0 || string.IsNullOrEmpty(reason) || reason.Length > 500)
            throw new ReceivingRuleException("Nhập số lượng thực tế và lý do điều chỉnh từ 1 đến 500 ký tự.");

        receipts.Reconcile(bookId, expectedStock, count, reason, actor, clock.GetUtcNow());
    }
}
