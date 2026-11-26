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
        Assert.Equal(OrderStatus.Shipped, service.Fulfill(id, "nhanvien", OrderAction.Completed, null, null, null));
        service.ConfirmReceipt(id, "khach");
        Assert.Equal("Paid", Assert.Single(service.AllOrders()).PaymentStatus);

        service.RequestReturn(id, 1, "khach", "Sách còn nguyên vẹn", "Refund");
        var request = Assert.Single(service.Returns("khach"));
        Assert.Throws<OrderRuleException>(() => service.ProcessReturnAsAdmin(request.Id, "admin", "Pending", "Receive"));
        service.ProcessReturnAsAdmin(request.Id, "admin", "Pending", "Approve");
        service.ProcessReturnAsAdmin(request.Id, "admin", "Approved", "Receive");
        Assert.Throws<OrderRuleException>(() => service.ProcessReturnAsAdmin(request.Id, "admin", "Approved", "Receive"));
        Assert.Equal(5, Stock(service, 1));
        Assert.Equal(130_000L, Assert.Single(service.Returns("khach")).RefundAmount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeliveryRequiresBothConfirmationsAndRecordsEachOnce(bool adminFirst)
    {
        var service = Start();
        var id = service.Place("khach", "Địa chỉ", new Dictionary<int, int> { [1] = 2 });
        Assert.Throws<OrderRuleException>(() => service.ConfirmReceipt(id, "khach"));
        Ship(service, id);
        Assert.Throws<OrderRuleException>(() => service.ConfirmReceipt(id, "nguoi_khac"));
        Assert.Throws<OrderRuleException>(() =>
            service.Fulfill(id, "admin", OrderAction.ReceiptConfirmed, null, null, null));

        OrderStatus ConfirmSide(OrderService current, bool admin) => admin
            ? current.Fulfill(id, "admin", OrderAction.DeliveryConfirmed, null, null, null)
            : current.ConfirmReceipt(id, "khach");

        Assert.Equal(OrderStatus.Shipped, ConfirmSide(service, adminFirst));

        var reopened = Start();
        var waiting = Assert.Single(reopened.CustomerOrders("khach"));
        Assert.Equal(OrderStatus.Shipped, waiting.Status);
        Assert.Null(waiting.CompletedAt);
        Assert.Equal("Unpaid", waiting.PaymentStatus);
        Assert.Equal(adminFirst, waiting.DeliveryConfirmed);
        Assert.Equal(!adminFirst, waiting.ReceiptConfirmed);
        Assert.False(waiting.CanReportDeliveryFailure);
        Assert.DoesNotContain(waiting.Events, entry => entry.Action == OrderAction.Completed);
        Assert.Throws<OrderRuleException>(() =>
            reopened.Fulfill(id, "admin", OrderAction.DeliveryFailed, null, null, "Không giao được"));
        Assert.Throws<OrderRuleException>(() => reopened.RequestReturn(id, 1, "khach", "Đổi sách", "Exchange"));

        Assert.Equal(OrderStatus.Shipped, ConfirmSide(reopened, adminFirst));
        Assert.Equal(OrderStatus.Completed, ConfirmSide(reopened, !adminFirst));

        var completed = Assert.Single(Start().CustomerOrders("khach"));
        Assert.Equal(OrderStatus.Completed, completed.Status);
        Assert.True(completed.DeliveryConfirmed);
        Assert.True(completed.ReceiptConfirmed);
        Assert.Equal("Paid", completed.PaymentStatus);

        var delivery = Assert.Single(completed.Events, entry => entry.Action == OrderAction.DeliveryConfirmed);
        var receipt = Assert.Single(completed.Events, entry => entry.Action == OrderAction.ReceiptConfirmed);
        var completion = Assert.Single(completed.Events, entry => entry.Action == OrderAction.Completed);
        AssertEvent(delivery, OrderAction.DeliveryConfirmed, "admin");
        AssertEvent(receipt, OrderAction.ReceiptConfirmed, "khach");
        Assert.Equal(adminFirst ? delivery : receipt, completed.Events[^3]);
        Assert.Equal(adminFirst ? receipt : delivery, completed.Events[^2]);
        Assert.Equal(completion, completed.Events[^1]);
        Assert.Equal(completed.Events[^2].OccurredAt, completed.CompletedAt);
        Assert.Equal(completion.OccurredAt, completed.CompletedAt);

        Assert.Equal(OrderStatus.Completed, ConfirmSide(reopened, true));
        Assert.Equal(OrderStatus.Completed, ConfirmSide(reopened, false));
        Assert.Throws<OrderRuleException>(() => reopened.ConfirmReceipt(id, "nguoi_khac"));
        var retried = Assert.Single(Start().CustomerOrders("khach"));
        Assert.Equal(completed.CompletedAt, retried.CompletedAt);
        Assert.Equal(completed.Events.ToArray(), retried.Events.ToArray());
    }

    [Fact]
    public async Task ConcurrentDeliveryAndReceiptConfirmationsCompleteOnlyOnce()
    {
        var service = Start();
        var id = service.Place("khach", "Địa chỉ", new Dictionary<int, int> { [1] = 2 });
        Ship(service, id);

        using var gate = new Barrier(2);
        OrderStatus Confirm(Func<OrderStatus> confirmation)
        {
            if (!gate.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Delivery confirmation workers did not start together.");

            return confirmation();
        }

        var results = await Task.WhenAll(
            Task.Run(() => Confirm(() => service.Fulfill(id, "admin", OrderAction.DeliveryConfirmed, null, null, null))),
            Task.Run(() => Confirm(() => service.ConfirmReceipt(id, "khach"))));

        Assert.Single(results, status => status == OrderStatus.Shipped);
        Assert.Single(results, status => status == OrderStatus.Completed);
        var completed = Assert.Single(Start().CustomerOrders("khach"));
        Assert.Equal(OrderStatus.Completed, completed.Status);
        Assert.True(completed.DeliveryConfirmed);
        Assert.True(completed.ReceiptConfirmed);
        Assert.Single(completed.Events, entry => entry.Action == OrderAction.Completed);
    }

    [Fact]
    public void ExchangeRequiresReplacementStockBeforeRestockingReturnedBooks()
    {
        var service = Start();
        var id = service.Place("khach", "Địa chỉ", new Dictionary<int, int> { [1] = 2 });
        var otherOrder = service.Place("another", "Địa chỉ", new Dictionary<int, int> { [1] = 3 });
        service.Confirm(id, "nhanvien");
        service.Confirm(otherOrder, "nhanvien");
        service.Fulfill(id, "nhanvien", OrderAction.Packing, null, null, null);
        service.Fulfill(id, "nhanvien", OrderAction.Shipped, "Đơn vị demo", "BOOK-123", null);
        service.Fulfill(id, "nhanvien", OrderAction.Completed, null, null, null);
        service.ConfirmReceipt(id, "khach");
        service.RequestReturn(id, 1, "khach", "Đổi bản khác", "Exchange");
        var request = Assert.Single(service.Returns("khach"));
        service.ProcessReturnAsAdmin(request.Id, "admin", "Pending", "Approve");

        Assert.Throws<OrderRuleException>(() =>
            service.ProcessReturnAsAdmin(request.Id, "admin", "Approved", "Receive"));
        Assert.Equal("Approved", Assert.Single(service.Returns("khach")).Status);
        Assert.Equal(0, Stock(service, 1));

        service.Cancel(otherOrder, "another");
        service.ProcessReturnAsAdmin(request.Id, "admin", "Approved", "Receive");

        Assert.Equal("Completed", Assert.Single(service.Returns("khach")).Status);
        Assert.Equal(3, Stock(service, 1));
    }

    [Fact]
    public void AdminReturnDecisionsApplyOnlyToTheStageDisplayed()
    {
        var service = Start();
        var id = service.Place("khach", "Địa chỉ", new Dictionary<int, int> { [1] = 2 });
        service.Confirm(id, "admin");
        service.Fulfill(id, "admin", OrderAction.Packing, null, null, null);
        service.Fulfill(id, "admin", OrderAction.Shipped, "Vận chuyển", "RT-2", null);
        service.Fulfill(id, "admin", OrderAction.Completed, null, null, null);
        service.ConfirmReceipt(id, "khach");
        service.RequestReturn(id, 1, "khach", "Sách còn nguyên vẹn", "Refund");
        var request = Assert.Single(service.Returns());

        service.ProcessReturnAsAdmin(request.Id, "admin", "Pending", "Approve");
        Assert.Throws<OrderRuleException>(() => service.ProcessReturnAsAdmin(request.Id, "admin", "Pending", "Reject"));
        Assert.Equal("Approved", Assert.Single(service.Returns()).Status);

        service.ProcessReturnAsAdmin(request.Id, "admin", "Approved", "Receive");
        Assert.Throws<OrderRuleException>(() => service.ProcessReturnAsAdmin(request.Id, "admin", "Approved", "Receive"));
        Assert.Equal("Completed", Assert.Single(service.Returns()).Status);
        Assert.Equal(5, Stock(service, 1));
    }

    [Fact]
    public void FailedDeliveryRestocksOnlyAfterWarehouseReceiptAndOnlyOnce()
    {
        var service = Start();
        var id = service.Place("khach", "Dia chi", new Dictionary<int, int> { [1] = 2 },
            paymentReference: "delivery-failure", online: true);
        Assert.Throws<OrderRuleException>(() => service.ReceiveFailedDelivery(id, "kho"));
        service.Confirm(id, "nhanvien");
        service.Fulfill(id, "nhanvien", OrderAction.Packing, null, null, null);
        service.Fulfill(id, "nhanvien", OrderAction.Shipped, "Van chuyen demo", "RT-1", null);

        Assert.Throws<OrderRuleException>(() =>
            service.Fulfill(id, "nhanvien", OrderAction.DeliveryFailed, null, null, " "));
        service.Fulfill(id, "nhanvien", OrderAction.DeliveryFailed, null, null, " Khach khong nhan ");

        var failed = Assert.Single(service.CustomerOrders("khach"));
        Assert.Equal(OrderStatus.DeliveryFailed, failed.Status);
        Assert.Equal("Khach khong nhan", failed.RejectionReason);
        Assert.Equal("RT-1", failed.TrackingCode);
        Assert.Equal("Paid", failed.PaymentStatus);
        Assert.Equal(3, Stock(service, 1));
        Assert.Throws<OrderRuleException>(() => service.Cancel(id, "khach"));
        Assert.Throws<OrderRuleException>(() =>
            service.Fulfill(id, "nhanvien", OrderAction.Completed, null, null, null));
        Assert.Throws<OrderRuleException>(() => service.ConfirmReceipt(id, "khach"));

        service.ReceiveFailedDelivery(id, "kho");
        Assert.Throws<OrderRuleException>(() => service.ReceiveFailedDelivery(id, "kho"));
        var returned = Assert.Single(Start().CustomerOrders("khach"));
        Assert.Equal(OrderStatus.ReturnedToWarehouse, returned.Status);
        Assert.Equal("Refunded", returned.PaymentStatus);
        Assert.Equal(5, Stock(service, 1));
        AssertEvent(returned.Events[^1], OrderAction.ReturnedToWarehouse, "kho");
    }

    private static void Ship(OrderService service, long id)
    {
        service.Confirm(id, "admin");
        service.Fulfill(id, "admin", OrderAction.Packing, null, null, null);
        service.Fulfill(id, "admin", OrderAction.Shipped, "Nhà vận chuyển demo", "BOOK-123", null);
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


