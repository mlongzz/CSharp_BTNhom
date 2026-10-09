using System.Globalization;
using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.Data.Sqlite;
using static Bookstore.Infrastructure.SqliteDatabase;

namespace Bookstore.Infrastructure;

public sealed partial class SqliteOrderRepository(SqliteDatabase database) : IOrderRepository
{
    public IReadOnlyList<Book> Books()
    {
        using var connection = database.Open();
        using var command = Command(connection, null, "SELECT id, title, price, stock FROM books WHERE active=1 ORDER BY id");
        using var reader = command.ExecuteReader();
        var books = new List<Book>();
        while (reader.Read())
            books.Add(ReadBook(reader));
        return books;
    }

    public IReadOnlyList<Order> Orders(string? customer)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = Command(connection, transaction, """
            SELECT id, customer, address, status, total, created_at, completed_at, carrier, tracking_code, rejection_reason, payment_method, payment_status, discount, shipping_fee FROM orders
            WHERE $customer IS NULL OR customer = $customer ORDER BY id DESC
            """, ("$customer", (object?)customer ?? DBNull.Value));
        var orders = new List<Order>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                orders.Add(ReadHeader(reader));
        }

        return orders.Select(order => Populate(connection, transaction, order)).ToList();
    }

    public IOrderTransaction BeginTransaction() => new SqliteOrderTransaction(database.Open());

    private static Book ReadBook(SqliteDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));

    private static Order ReadHeader(SqliteDataReader reader) =>
        new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            Enum.Parse<OrderStatus>(reader.GetString(3)), reader.GetInt64(4), [], [])
        {
            CreatedAt = ReadDate(reader, 5),
            CompletedAt = ReadDate(reader, 6),
            Carrier = ReadNullable(reader, 7),
            TrackingCode = ReadNullable(reader, 8),
            RejectionReason = ReadNullable(reader, 9),
            PaymentMethod = reader.GetString(10),
            PaymentStatus = reader.GetString(11),
            Discount = reader.GetInt64(12),
            ShippingFee = reader.GetInt64(13)
        };

    private static string? ReadNullable(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : reader.GetString(column);

    private static DateTimeOffset? ReadDate(SqliteDataReader reader, int column) =>
        ReadNullable(reader, column) is string text ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) : null;

    private static string Timestamp(DateTimeOffset time) => time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private const string ReturnQuery = """
        SELECT r.id,r.order_id,r.book_id,r.quantity,r.reason,r.status,r.outcome,o.customer,b.title,r.refund_amount
        FROM return_requests r JOIN orders o ON o.id=r.order_id JOIN books b ON b.id=r.book_id
        """;

    public IReadOnlyList<ReturnRequest> Returns(string? customer)
    {
        using var connection = database.Open();
        using var query = Command(connection, null, ReturnQuery +
            " WHERE $customer IS NULL OR o.customer=$customer ORDER BY r.id DESC",
            ("$customer", (object?)customer ?? DBNull.Value));
        using var reader = query.ExecuteReader();
        var requests = new List<ReturnRequest>();
        while (reader.Read())
            requests.Add(ReadReturn(reader));
        return requests;
    }

    private static ReturnRequest ReadReturn(SqliteDataReader reader) =>
        new(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2),reader.GetInt32(3),
            reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetInt64(9));

    private static Order Populate(SqliteConnection connection, SqliteTransaction transaction, Order order)
    {
        using var lineQuery = Command(connection, transaction, """
            SELECT b.id, b.title, l.quantity, l.price FROM order_lines l
            JOIN books b ON b.id = l.book_id WHERE l.order_id = $id ORDER BY b.id
            """, ("$id", order.Id));
        var lines = new List<OrderLine>();
        using (var reader = lineQuery.ExecuteReader())
        {
            while (reader.Read())
                lines.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        }

        using var eventQuery = Command(connection, transaction, """
            SELECT action, actor, occurred_at FROM order_events WHERE order_id = $id ORDER BY id
            """, ("$id", order.Id));
        var events = new List<OrderEvent>();
        using var eventReader = eventQuery.ExecuteReader();
        while (eventReader.Read())
            events.Add(new(Enum.Parse<OrderAction>(eventReader.GetString(0)), eventReader.GetString(1),
                DateTimeOffset.ParseExact(eventReader.GetString(2), "O", CultureInfo.InvariantCulture)));

        return order with { Lines = lines, Events = events };
    }

    private sealed class SqliteOrderTransaction : IOrderTransaction
    {
        private readonly SqliteConnection connection;
        private readonly SqliteTransaction transaction;

        public SqliteOrderTransaction(SqliteConnection connection)
        {
            this.connection = connection;
            try
            {
                // An immediate write transaction serializes state checks with the writes they guard.
                transaction = connection.BeginTransaction(deferred: false);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public Book? FindBook(int id)
        {
            using var command = Command(connection, transaction,
                "SELECT id, title, price, stock FROM books WHERE id = $id AND active=1", ("$id", id));
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadBook(reader) : null;
        }

        public Order? FindOrder(long id)
        {
            using var command = Command(connection, transaction, """
                SELECT id, customer, address, status, total, created_at, completed_at, carrier, tracking_code, rejection_reason, payment_method, payment_status, discount, shipping_fee FROM orders WHERE id = $id
                """, ("$id", id));
            Order order;
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                    return null;
                order = ReadHeader(reader);
            }
            return Populate(connection, transaction, order);
        }

        public void RejectPayment(string reference)
        {
            using var command = Command(connection, transaction, """
                INSERT OR IGNORE INTO rejected_payments(reference)
                SELECT $reference WHERE NOT EXISTS (SELECT 1 FROM orders WHERE payment_reference=$reference)
                """, ("$reference", reference));
            command.ExecuteNonQuery();
        }

        public bool PaymentRejected(string reference)
        {
            using var command = Command(connection, transaction,
                "SELECT 1 FROM rejected_payments WHERE reference=$reference", ("$reference", reference));
            return command.ExecuteScalar() is not null;
        }

        public long? FindPayment(string customer, string reference)
        {
            using var query = Command(connection, transaction,
                "SELECT id FROM orders WHERE customer=$customer AND payment_reference=$reference",
                ("$customer", customer), ("$reference", reference));
            return query.ExecuteScalar() is long id ? id : null;
        }

        public long VoucherDiscount(string code, long subtotal, bool redeem)
        {
            using var query = Command(connection, transaction, """
                SELECT discount_type, value FROM vouchers
                WHERE code=$code AND used_count < max_uses AND minimum_total <= $subtotal
                    AND datetime(starts_at) <= datetime('now') AND datetime(ends_at) >= datetime('now')
                """, ("$code", code), ("$subtotal", subtotal));
            long discount;
            using (var reader = query.ExecuteReader())
            {
                if (!reader.Read())
                    throw new OrderRuleException("Mã giảm giá không hợp lệ hoặc đã hết hạn.");
                var value = reader.GetInt64(1);
                discount = reader.GetString(0) == "Percent"
                    ? (long)(subtotal * (decimal)value / 100) : value;
            }
            if (redeem)
            {
                using var update = Command(connection, transaction,
                    "UPDATE vouchers SET used_count=used_count+1 WHERE code=$code", ("$code", code));
                update.ExecuteNonQuery();
            }
            return Math.Min(discount, subtotal);
        }

        public long InsertOrder(string customer, string address, OrderQuote quote, IReadOnlyList<OrderLine> lines,
            DateTimeOffset created, string? paymentReference, bool online)
        {
            using var insert = Command(connection, transaction, """
                INSERT INTO orders(customer,address,status,total,created_at,payment_method,payment_status,
                    payment_reference,discount,shipping_fee)
                VALUES ($customer,$address,'Pending',$total,$at,$method,$payment,$reference,$discount,$shipping)
                RETURNING id
                """, ("$customer", customer), ("$address", address), ("$total", quote.Total), ("$at", Timestamp(created)),
                ("$method", online ? "Online" : "Cod"),
                ("$payment", online ? "Paid" : "Unpaid"),
                ("$reference", (object?)paymentReference ?? DBNull.Value), ("$discount", quote.Discount),
                ("$shipping", quote.ShippingFee));
            var id = (long)insert.ExecuteScalar()!;
            foreach (var line in lines)
            {
                using var command = Command(connection, transaction, """
                    INSERT INTO order_lines(order_id, book_id, quantity, price)
                    VALUES ($order, $book, $quantity, $price)
                    """, ("$order", id), ("$book", line.BookId), ("$quantity", line.Quantity),
                    ("$price", line.Price));
                command.ExecuteNonQuery();
            }
            return id;
        }

        public void UpdateStatus(long id, OrderStatus status, DateTimeOffset at, string? carrier = null,
            string? tracking = null, string? rejection = null)
        {
            using var command = Command(connection, transaction, """
                UPDATE orders SET status=$status, carrier=COALESCE($carrier,carrier),
                    tracking_code=COALESCE($tracking,tracking_code),
                    rejection_reason=COALESCE($rejection,rejection_reason),
                    completed_at=CASE WHEN $status='Completed' THEN $at ELSE completed_at END,
                    payment_status=CASE WHEN $status='Completed' THEN 'Paid'
                        WHEN $status='Cancelled' AND payment_status='Paid' THEN 'Refunded' ELSE payment_status END
                WHERE id=$id
                """, ("$id", id), ("$status", status.ToString()), ("$at", Timestamp(at)),
                ("$carrier", (object?)carrier ?? DBNull.Value), ("$tracking", (object?)tracking ?? DBNull.Value),
                ("$rejection", (object?)rejection ?? DBNull.Value));
            command.ExecuteNonQuery();
        }

        public void StockMovement(int bookId, int delta, string kind, long reference, string actor, DateTimeOffset at) =>
            SqliteReceiptRepository.RecordStockMovement(connection, transaction, bookId, delta, kind, reference, actor, at);

        public void Notify(string customer, string subject, string message) =>
            SqliteCustomerAccounts.QueueEmail(connection, transaction, customer, subject, message);

        public void InsertReturn(long orderId, int bookId, int quantity, string reason, string outcome, DateTimeOffset at)
        {
            using var insert = Command(connection, transaction, """
                INSERT INTO return_requests(order_id,book_id,quantity,reason,status,outcome,requested_at)
                VALUES ($order,$book,$quantity,$reason,'Pending',$outcome,$at)
                ON CONFLICT(order_id,book_id) DO NOTHING
                """, ("$order", orderId), ("$book", bookId), ("$quantity", quantity), ("$reason", reason),
                ("$outcome", outcome), ("$at", Timestamp(at)));
            if (insert.ExecuteNonQuery() != 1)
                throw new OrderRuleException("Đã có yêu cầu đổi trả cho sách này trong đơn.");
        }

        public ReturnRequest? FindReturn(long id)
        {
            using var query = Command(connection, transaction, ReturnQuery + " WHERE r.id=$id", ("$id", id));
            using var reader = query.ExecuteReader();
            return reader.Read() ? ReadReturn(reader) : null;
        }

        public void UpdateReturn(long id, string status, DateTimeOffset at, long refundAmount)
        {
            using var update = Command(connection, transaction, """
                UPDATE return_requests SET status=$status, completed_at=CASE WHEN $status='Completed' THEN $at END,
                    refund_amount=$refund
                WHERE id=$id
                """, ("$id", id), ("$status", status), ("$at", Timestamp(at)), ("$refund", refundAmount));
            update.ExecuteNonQuery();
        }

        public bool TryDeductStock(int bookId, int quantity)
        {
            using var command = Command(connection, transaction, """
                UPDATE books SET stock = stock - $quantity WHERE id = $book AND stock >= $quantity
                """, ("$quantity", quantity), ("$book", bookId));
            return command.ExecuteNonQuery() == 1;
        }

        public void RestoreStock(int bookId, int quantity)
        {
            using var command = Command(connection, transaction,
                "UPDATE books SET stock = stock + $quantity WHERE id = $book AND stock + $quantity <= 2147483647",
                ("$quantity", quantity), ("$book", bookId));
            if (command.ExecuteNonQuery() != 1)
                throw new OrderRuleException("Số lượng tồn sau khi hoàn kho vượt giới hạn.");
        }

        public void AppendEvent(long orderId, OrderEvent entry)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO order_events(order_id, action, actor, occurred_at)
                VALUES ($order, $action, $actor, $occurred)
                """, ("$order", orderId), ("$action", entry.Action.ToString()), ("$actor", entry.Actor),
                ("$occurred", entry.OccurredAt.ToString("O", CultureInfo.InvariantCulture)));
            command.ExecuteNonQuery();
        }

        public void Commit() => transaction.Commit();

        public void Dispose()
        {
            transaction.Dispose();
            connection.Dispose();
        }
    }
}
