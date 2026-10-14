using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Bookstore.Tests;

public sealed class OrderPersistenceTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("bookstore-test-").FullName;

    private string DatabasePath => Path.Combine(directory, "test.db");

    private OrderService Start()
    {
        var database = new SqliteDatabase(DatabasePath);
        database.Initialize();
        return new(new SqliteOrderRepository(database), TimeProvider.System);
    }

    [Fact]
    public void PlacementConfirmationAndCancellationMaintainStockAndHistory()
    {
        var service = Start();

        var id = service.Place("khach", "  12 An Dương Vương ", new Dictionary<int, int> { [1] = 2 });
        var placed = Assert.Single(service.CustomerOrders("khach"));
        Assert.Equal("12 An Dương Vương", placed.Address);
        Assert.Equal(OrderStatus.Pending, placed.Status);
        Assert.Equal(145_000L, placed.Total);
        Assert.Equal(5, Stock(service, 1));

        service.Confirm(id, "nhanvien");
        Assert.Equal(3, Stock(service, 1));
        Assert.Equal(OrderStatus.Confirmed, Assert.Single(service.AllOrders()).Status);

        service.Cancel(id, "khach");
        var cancelled = Assert.Single(Start().CustomerOrders("khach"));
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(5, Stock(service, 1));
        Assert.Collection(cancelled.Events,
            entry => AssertEvent(entry, OrderAction.Placed, "khach"),
            entry => AssertEvent(entry, OrderAction.Confirmed, "nhanvien"),
            entry => AssertEvent(entry, OrderAction.Cancelled, "khach"));
        Assert.Throws<OrderRuleException>(() => service.Cancel(id, "khach"));
        Assert.Equal(5, Stock(service, 1));
    }

    [Fact]
    public void PendingCancellationDoesNotAddStockAndRequiresOwner()
    {
        var service = Start();
        var id = service.Place("khach", "Địa chỉ", new Dictionary<int, int> { [1] = 2 });

        Assert.Empty(service.CustomerOrders("nguoi_khac"));
        Assert.Throws<OrderRuleException>(() => service.Cancel(id, "nguoi_khac"));
        Assert.Equal(OrderStatus.Pending, Assert.Single(service.AllOrders()).Status);
        service.Cancel(id, "khach");
        Assert.Equal(5, Stock(service, 1));
        Assert.Throws<OrderRuleException>(() => service.Confirm(id, "nhanvien"));
    }

    [Fact]
    public void InsufficientSecondLineRollsBackFirstStockChangeAndEvent()
    {
        var service = Start();
        var competing = service.Place("khach", "Địa chỉ", new Dictionary<int, int> { [2] = 7 });
        var order = service.Place("khach", "Địa chỉ", new Dictionary<int, int> { [1] = 2, [2] = 2 });
        service.Confirm(competing, "nhanvien");

        Assert.Throws<OrderRuleException>(() => service.Confirm(order, "nhanvien"));

        Assert.Equal(5, Stock(service, 1));
        Assert.Equal(1, Stock(service, 2));
        var pending = Assert.Single(service.AllOrders(), item => item.Id == order);
        Assert.Equal(OrderStatus.Pending, pending.Status);
        AssertEvent(Assert.Single(pending.Events), OrderAction.Placed, "khach");
    }

    [Fact]
    public async Task ConcurrentConfirmationDeductsStockOnlyOnce()
    {
        var service = Start();
        var id = service.Place("khach", "Địa chỉ", new Dictionary<int, int> { [1] = 2 });
        using var gate = new Barrier(2);
        bool Confirm()
        {
            if (!gate.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Confirmation workers did not start together.");

            try
            {
                service.Confirm(id, "nhanvien");
                return true;
            }
            catch (OrderRuleException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(Task.Run(Confirm), Task.Run(Confirm));
        Assert.Single(results, success => success);
        Assert.Equal(3, Stock(service, 1));
        Assert.Equal(2, Assert.Single(service.AllOrders()).Events.Count);
    }

    [Fact]
    public void MigratesLegacyDatabaseWithoutLosingAccountsOrdersOrStock()
    {
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE customers(username TEXT PRIMARY KEY, password_hash TEXT NOT NULL);
                CREATE TABLE books(id INTEGER PRIMARY KEY, title TEXT NOT NULL, price INTEGER NOT NULL, stock INTEGER NOT NULL);
                CREATE TABLE orders(id INTEGER PRIMARY KEY, customer TEXT NOT NULL, address TEXT NOT NULL,
                    status TEXT NOT NULL CHECK(status IN ('Pending', 'Confirmed')), total INTEGER NOT NULL);
                CREATE TABLE order_lines(order_id INTEGER NOT NULL REFERENCES orders(id),
                    book_id INTEGER NOT NULL REFERENCES books(id), quantity INTEGER NOT NULL, price INTEGER NOT NULL,
                    PRIMARY KEY(order_id, book_id));
                INSERT INTO customers VALUES ('khach_cu', 'previous-hash');
                INSERT INTO books VALUES (1, 'Dế Mèn', 65000, 3);
                INSERT INTO orders VALUES (42, 'khach_cu', 'Địa chỉ', 'Confirmed', 130000);
                INSERT INTO order_lines VALUES (42, 1, 2, 65000);
                """;
            command.ExecuteNonQuery();
        }

        var service = Start();
        var accountStore = new SqliteCustomerAccounts(new SqliteDatabase(DatabasePath));
        Assert.Equal("previous-hash", accountStore.PasswordHash("khach_cu"));
        var order = Assert.Single(service.CustomerOrders("khach_cu"));
        Assert.Equal(42L, order.Id);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Empty(order.Events);
        Assert.Equal(2, Assert.Single(order.Lines).Quantity);
        Assert.Equal(3, Stock(service, 1));

        service.Cancel(order.Id, "khach_cu");
        Assert.Equal(5, Stock(service, 1));
        Assert.Equal(OrderStatus.Cancelled, Assert.Single(Start().AllOrders()).Status);
    }

    [Fact]
    public void OnlinePaymentFailureIsTerminalAndSuccessIsIdempotent()
    {
        var service = Start();
        var quantities = new Dictionary<int, int> { [1] = 2 };
        service.RejectPayment("failed-payment");
        Assert.Throws<OrderRuleException>(() =>
            service.Place("khach", "Địa chỉ", quantities, paymentReference: "failed-payment", online: true));
        Assert.Empty(service.AllOrders());

        var quote = service.Quote("Địa chỉ", quantities, null);
        var id = service.Place("khach", "Địa chỉ", quantities, paymentReference: "paid-payment",
            expectedTotal: quote.Total, online: true);
        var retry = service.Place("khach", "Địa chỉ", quantities, paymentReference: "paid-payment",
            expectedTotal: quote.Total, online: true);
        Assert.Equal(id, retry);
        Assert.Equal("Paid", Assert.Single(service.AllOrders()).PaymentStatus);
        Assert.Equal(5, Stock(service, 1));
    }

    [Fact]
    public void DeliveryAndReturnRequireOrderedTransitionsAndRestockOnce()
    {
        var service = Start();
        var id = service.Place("khach", "Địa chỉ", new Dictionary<int, int> { [1] = 2 });
        Assert.Throws<OrderRuleException>(() =>
            service.Fulfill(id, "nhanvien", OrderAction.Completed, null, null, null));
        service.Confirm(id, "nhanvien");
        service.Fulfill(id, "nhanvien", OrderAction.Packing, null, null, null);
        Assert.Throws<OrderRuleException>(() => service.Cancel(id, "khach"));
        service.Fulfill(id, "nhanvien", OrderAction.Shipped, "Nhà vận chuyển demo", "QA-123", null);
        Assert.Equal("Unpaid", Assert.Single(service.AllOrders()).PaymentStatus);
        service.Fulfill(id, "nhanvien", OrderAction.Completed, null, null, null);
        Assert.Equal("Paid", Assert.Single(service.AllOrders()).PaymentStatus);

        service.RequestReturn(id, 1, "khach", "Sách còn nguyên vẹn", "Refund");
        var request = Assert.Single(service.Returns("khach"));
        Assert.Throws<OrderRuleException>(() => service.ProcessReturn(request.Id, "kho", "Warehouse", "Receive"));
        service.ProcessReturn(request.Id, "nhanvien", "Sales", "Approve");
        service.ProcessReturn(request.Id, "kho", "Warehouse", "Receive");
        Assert.Throws<OrderRuleException>(() => service.ProcessReturn(request.Id, "kho", "Warehouse", "Receive"));
        Assert.Equal(5, Stock(service, 1));
        Assert.Equal(130_000L, Assert.Single(service.Returns("khach")).RefundAmount);
    }

    private static int Stock(OrderService service, int id) => service.Books().Single(book => book.Id == id).Stock;

    private static void AssertEvent(OrderEvent entry, OrderAction action, string actor)
    {
        Assert.Equal(action, entry.Action);
        Assert.Equal(actor, entry.Actor);
        Assert.NotEqual(default, entry.OccurredAt);
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


