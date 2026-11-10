using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Bookstore.Tests;

public sealed class ReceivingTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("bookstore-receiving-").FullName;
    private string DatabasePath => Path.Combine(directory, "test.db");

    private ReceivingService Start()
    {
        var database = new SqliteDatabase(DatabasePath);
        database.Initialize();
        return new(new SqliteReceiptRepository(database), TimeProvider.System);
    }

    [Fact]
    public void ReceiptRequiresApprovalAndAddsOnlyActuallyReceivedStock()
    {
        var service = Start();
        service.SaveSupplier(0, " Nhà xuất bản Trẻ ", "0901234567", "nxb@example.test", "TP.HCM");
        var supplier = Assert.Single(service.Suppliers());
        var id = service.Create("kho", supplier.Id, new Dictionary<int, PurchaseInput> { [1] = new(3, 10_000), [2] = new(4, 20_000) });
        var actual = new Dictionary<int, int> { [1] = 2, [2] = 4 };

        Assert.Throws<ReceivingRuleException>(() => service.Receive(id, "kho", actual, "Thiếu một cuốn"));
        Assert.Equal(5, service.Inventory().Single(book => book.Id == 1).Stock);
        service.Approve(id, "quantri");
        Assert.Throws<ReceivingRuleException>(() => service.Receive(id, "kho", actual, null));
        Assert.Equal(PurchaseStatus.Approved, Assert.Single(service.Purchases()).Status);
        service.Receive(id, "kho", actual, "Thiếu một cuốn");

        var reopened = Start();
        var purchase = Assert.Single(reopened.Purchases());
        Assert.Equal(PurchaseStatus.Received, purchase.Status);
        Assert.Equal("Nhà xuất bản Trẻ", purchase.Supplier);
        Assert.Equal("quantri", purchase.ApprovedBy);
        Assert.Equal("kho", purchase.ReceivedBy);
        Assert.Equal(110_000, purchase.Total);
        Assert.Equal(new int?[] { 2, 4 }, purchase.Lines.Select(line => line.ReceivedQuantity));
        Assert.Equal(7, reopened.Inventory().Single(book => book.Id == 1).Stock);
        Assert.Equal(12, reopened.Inventory().Single(book => book.Id == 2).Stock);
        Assert.Equal(2, reopened.Movements(null).Count);
        Assert.Throws<ReceivingRuleException>(() => reopened.Receive(id, "kho", actual, "Thiếu một cuốn"));
        Assert.Throws<ReceivingRuleException>(() => reopened.DeleteSupplier(supplier.Id));
    }

    [Fact]
    public void UnknownSecondBookRollsBackWholePurchase()
    {
        var service = Start();
        service.SaveSupplier(0, "NXB", null, null, null);
        var supplier = Assert.Single(service.Suppliers());
        var lines = new Dictionary<int, PurchaseInput> { [1] = new(3, 10_000), [999] = new(1, 10_000) };
        Assert.Throws<ReceivingRuleException>(() => service.Create("kho", supplier.Id, lines));
        Assert.Empty(service.Purchases());
        Assert.Equal(5, service.Inventory().Single(book => book.Id == 1).Stock);
    }

    [Fact]
    public void ReconcileRecordsAdjustmentAndThreshold()
    {
        var service = Start();
        service.SetMinimum(1, 4);
        service.Reconcile(1, 3, "Đếm tại kệ", "kho");
        service.Reconcile(1, 3, "Đếm lại", "kho");
        var book = service.Inventory().Single(item => item.Id == 1);
        Assert.Equal(3, book.Stock);
        Assert.Equal(4, book.Minimum);
        var change = Assert.Single(service.Movements(1));
        Assert.Equal(-2, change.Delta);
        Assert.Equal("Adjustment", change.Kind);
        Assert.Equal("Đếm tại kệ", change.Note);
    }

    [Fact]
    public void V2ReceiptMigrationKeepsAlreadyReceivedStockAndHistory()
    {
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE customers(username TEXT PRIMARY KEY, password_hash TEXT NOT NULL);
                CREATE TABLE books(id INTEGER PRIMARY KEY, title TEXT NOT NULL, price INTEGER NOT NULL, stock INTEGER NOT NULL);
                CREATE TABLE orders(id INTEGER PRIMARY KEY, customer TEXT NOT NULL, address TEXT NOT NULL,
                    status TEXT NOT NULL, total INTEGER NOT NULL);
                CREATE TABLE order_lines(order_id INTEGER NOT NULL REFERENCES orders(id),
                    book_id INTEGER NOT NULL REFERENCES books(id), quantity INTEGER NOT NULL, price INTEGER NOT NULL,
                    PRIMARY KEY(order_id, book_id));
                CREATE TABLE order_events(id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES orders(id),
                    action TEXT NOT NULL, actor TEXT NOT NULL, occurred_at TEXT NOT NULL);
                CREATE TABLE receipts(id INTEGER PRIMARY KEY, supplier TEXT NOT NULL, staff TEXT NOT NULL, received_at TEXT NOT NULL);
                CREATE TABLE receipt_lines(receipt_id INTEGER NOT NULL REFERENCES receipts(id), book_id INTEGER NOT NULL REFERENCES books(id),
                    quantity INTEGER NOT NULL, PRIMARY KEY(receipt_id, book_id));
                CREATE INDEX idx_orders_customer ON orders(customer);
                CREATE INDEX idx_order_events_order ON order_events(order_id);
                INSERT INTO books VALUES (1, 'Dế Mèn phiêu lưu ký', 65000, 9);
                INSERT INTO receipts VALUES (15, 'Nhà cung cấp cũ', 'nhanvien', '2026-10-26T01:02:03.0000000+00:00');
                INSERT INTO receipt_lines VALUES (15, 1, 4);
                INSERT INTO orders VALUES (8, 'khach', 'Địa chỉ', 'Cancelled', 65000);
                INSERT INTO order_lines VALUES (8, 1, 1, 65000);
                INSERT INTO order_events VALUES (1, 8, 'Placed', 'khach', '2026-10-27T01:00:00.0000000+00:00');
                INSERT INTO order_events VALUES (2, 8, 'Confirmed', 'nhanvien', '2026-10-27T01:01:00.0000000+00:00');
                INSERT INTO order_events VALUES (3, 8, 'Cancelled', 'khach', '2026-10-27T01:02:00.0000000+00:00');
                PRAGMA user_version = 2;
                """;
            command.ExecuteNonQuery();
        }

        var service = Start();
        var previous = Assert.Single(service.Purchases());
        Assert.Equal(15L, previous.Id);
        Assert.Equal("Nhà cung cấp cũ", previous.Supplier);
        Assert.Equal(4, Assert.Single(previous.Lines).ReceivedQuantity);
        Assert.Equal(9, Assert.Single(service.Inventory()).Stock);
        Assert.Equal(new[] { 1, -1, 4 }, service.Movements(null).Select(entry => entry.Delta));
    }

    public void Dispose()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true
        }.ToString());
        SqliteConnection.ClearPool(connection);
        Directory.Delete(directory, recursive: true);
    }
}
