using System.Globalization;
using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.Data.Sqlite;
using static Bookstore.Infrastructure.SqliteDatabase;

namespace Bookstore.Infrastructure;

public sealed class SqliteReceiptRepository(SqliteDatabase database) : IReceiptRepository
{
    internal static void Migrate(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = Command(connection, transaction, """
            CREATE TABLE suppliers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL, phone TEXT NOT NULL DEFAULT '', email TEXT NOT NULL DEFAULT '', address TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE purchase_orders (
                id INTEGER PRIMARY KEY,
                supplier_id INTEGER NOT NULL REFERENCES suppliers(id),
                created_by TEXT NOT NULL, created_at TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('Pending', 'Approved', 'Received')),
                approved_by TEXT, received_by TEXT, received_at TEXT, note TEXT
            );
            CREATE TABLE purchase_order_lines (
                purchase_id INTEGER NOT NULL REFERENCES purchase_orders(id),
                book_id INTEGER NOT NULL REFERENCES books(id),
                quantity INTEGER NOT NULL CHECK(quantity > 0),
                unit_cost INTEGER NOT NULL CHECK(unit_cost >= 0),
                received_quantity INTEGER CHECK(received_quantity >= 0),
                PRIMARY KEY(purchase_id, book_id)
            );
            CREATE TABLE inventory_thresholds (
                book_id INTEGER PRIMARY KEY REFERENCES books(id) ON DELETE CASCADE,
                minimum INTEGER NOT NULL CHECK(minimum >= 0)
            );
            CREATE TABLE stock_movements (
                id INTEGER PRIMARY KEY,
                book_id INTEGER NOT NULL REFERENCES books(id),
                delta INTEGER NOT NULL CHECK(delta <> 0), kind TEXT NOT NULL,
                ref_id INTEGER, actor TEXT NOT NULL, occurred_at TEXT NOT NULL, note TEXT
            );
            CREATE INDEX idx_stock_movements_book ON stock_movements(book_id, id);
            CREATE INDEX idx_purchase_orders_supplier ON purchase_orders(supplier_id);
            CREATE INDEX idx_purchase_lines_book ON purchase_order_lines(book_id);

            INSERT INTO suppliers(name) SELECT DISTINCT supplier FROM receipts;
            INSERT INTO purchase_orders(id, supplier_id, created_by, created_at, status, received_by, received_at, note)
                SELECT r.id, s.id, r.staff, r.received_at, 'Received', r.staff, r.received_at,
                       'Phiếu từ phiên bản trước, chưa có dữ liệu giá nhập'
                FROM receipts r JOIN suppliers s ON s.name = r.supplier;
            INSERT INTO purchase_order_lines(purchase_id, book_id, quantity, unit_cost, received_quantity)
                SELECT receipt_id, book_id, quantity, 0, quantity FROM receipt_lines;
            INSERT INTO stock_movements(book_id, delta, kind, ref_id, actor, occurred_at)
                SELECT l.book_id, l.quantity, 'Receipt', l.receipt_id, r.staff, r.received_at
                FROM receipt_lines l JOIN receipts r ON r.id = l.receipt_id ORDER BY r.id;
            INSERT INTO stock_movements(book_id, delta, kind, ref_id, actor, occurred_at)
                SELECT l.book_id,
                       CASE e.action WHEN 'Confirmed' THEN -l.quantity ELSE l.quantity END,
                       CASE e.action WHEN 'Confirmed' THEN 'Sale' ELSE 'Cancellation' END,
                       e.order_id, e.actor, e.occurred_at
                FROM order_events e JOIN order_lines l ON l.order_id = e.order_id
                WHERE e.action = 'Confirmed' OR (e.action = 'Cancelled' AND EXISTS (
                    SELECT 1 FROM order_events earlier
                    WHERE earlier.order_id = e.order_id AND earlier.action = 'Confirmed' AND earlier.id < e.id
                ))
                ORDER BY e.id;
            """);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Supplier> Suppliers()
    {
        using var connection = database.Open();
        using var command = Command(connection, null, "SELECT id, name, phone, email, address FROM suppliers ORDER BY name, id");
        using var reader = command.ExecuteReader();
        var suppliers = new List<Supplier>();
        while (reader.Read())
            suppliers.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return suppliers;
    }

    public void SaveSupplier(Supplier supplier)
    {
        using var connection = database.Open();
        var sql = supplier.Id == 0
            ? "INSERT INTO suppliers(name, phone, email, address) VALUES ($name, $phone, $email, $address)"
            : "UPDATE suppliers SET name = $name, phone = $phone, email = $email, address = $address WHERE id = $id";
        using var command = Command(connection, null, sql, ("$id", supplier.Id), ("$name", supplier.Name),
            ("$phone", supplier.Phone), ("$email", supplier.Email), ("$address", supplier.Address));
        if (command.ExecuteNonQuery() != 1)
            throw new ReceivingRuleException("Không tìm thấy nhà cung cấp.");
    }

    public void DeleteSupplier(int id)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            DELETE FROM suppliers WHERE id = $id
            AND NOT EXISTS (SELECT 1 FROM purchase_orders WHERE supplier_id = $id)
            """, ("$id", id));
        if (command.ExecuteNonQuery() != 1)
            throw new ReceivingRuleException("Nhà cung cấp không tồn tại hoặc đang được phiếu nhập tham chiếu.");
    }

    public IReadOnlyList<PurchaseOrder> Purchases()
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            SELECT p.id, s.name, p.created_by, p.created_at, p.status, p.approved_by, p.received_by, p.received_at, p.note,
                   b.id, b.title, l.quantity, l.unit_cost, l.received_quantity
            FROM purchase_orders p JOIN suppliers s ON s.id = p.supplier_id
            JOIN purchase_order_lines l ON l.purchase_id = p.id JOIN books b ON b.id = l.book_id
            ORDER BY p.id DESC, b.id
            """);
        using var reader = command.ExecuteReader();
        var orders = new List<PurchaseOrder>();
        List<PurchaseLine>? lines = null;
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            if (orders.Count == 0 || orders[^1].Id != id)
            {
                lines = [];
                orders.Add(new(id, reader.GetString(1), reader.GetString(2), ParseDate(reader.GetString(3)),
                    Enum.Parse<PurchaseStatus>(reader.GetString(4)), OptionalText(reader, 5), OptionalText(reader, 6),
                    reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)), OptionalText(reader, 8), lines));
            }

            lines!.Add(new(reader.GetInt32(9), reader.GetString(10), reader.GetInt32(11), reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13)));
        }
        return orders;
    }

    public long Create(string staff, int supplierId, IReadOnlyDictionary<int, PurchaseInput> lines, DateTimeOffset now)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var insert = Command(connection, transaction, """
            INSERT INTO purchase_orders(supplier_id, created_by, created_at, status)
            SELECT id, $staff, $date, 'Pending' FROM suppliers WHERE id = $supplier
            RETURNING id
            """, ("$supplier", supplierId), ("$staff", staff), ("$date", FormatDate(now)));
        if (insert.ExecuteScalar() is not long id)
            throw new ReceivingRuleException("Không tìm thấy nhà cung cấp.");

        foreach (var (bookId, line) in lines)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO purchase_order_lines(purchase_id, book_id, quantity, unit_cost)
                SELECT $purchase, id, $quantity, $cost FROM books WHERE id = $book
                """, ("$purchase", id), ("$book", bookId), ("$quantity", line.Quantity), ("$cost", line.UnitCost));
            if (command.ExecuteNonQuery() != 1)
                throw new ReceivingRuleException($"Không tìm thấy sách mã {bookId}.");
        }

        transaction.Commit();
        return id;
    }

    public void Approve(long id, string admin)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            UPDATE purchase_orders SET status = 'Approved', approved_by = $admin WHERE id = $id AND status = 'Pending'
            """, ("$id", id), ("$admin", admin));
        if (command.ExecuteNonQuery() != 1)
            throw new ReceivingRuleException("Chỉ có thể duyệt phiếu đang chờ.");
    }

    public void Receive(long id, string staff, IReadOnlyDictionary<int, int> quantities, string? note, DateTimeOffset now)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var update = Command(connection, transaction, """
            UPDATE purchase_orders SET status = 'Received', received_by = $staff, received_at = $date, note = $note
            WHERE id = $id AND status = 'Approved'
            """, ("$id", id), ("$staff", staff), ("$date", FormatDate(now)), ("$note", (object?)note ?? DBNull.Value));
        if (update.ExecuteNonQuery() != 1)
            throw new ReceivingRuleException("Chỉ có thể nhập kho phiếu đã duyệt.");

        using var select = Command(connection, transaction,
            "SELECT book_id, quantity FROM purchase_order_lines WHERE purchase_id = $id", ("$id", id));
        var planned = new Dictionary<int, int>();
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
                planned.Add(reader.GetInt32(0), reader.GetInt32(1));
        }

        if (quantities.Count != planned.Count || quantities.Keys.Any(book => !planned.ContainsKey(book)))
            throw new ReceivingRuleException("Nhập số lượng thực nhận cho tất cả đầu sách trong phiếu.");
        if (quantities.Any(line => line.Value != planned[line.Key]) && string.IsNullOrWhiteSpace(note))
            throw new ReceivingRuleException("Ghi chú lý do khi số lượng thực nhận khác số lượng trên phiếu.");

        foreach (var (bookId, quantity) in quantities)
        {
            if (quantity > 0)
            {
                ChangeStock(connection, transaction, bookId, quantity);
                RecordStockMovement(connection, transaction, bookId, quantity, "Receipt", id, staff, now);
            }

            using var line = Command(connection, transaction, """
                UPDATE purchase_order_lines SET received_quantity = $quantity
                WHERE purchase_id = $id AND book_id = $book
                """, ("$id", id), ("$book", bookId), ("$quantity", quantity));
            line.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<InventoryBook> Inventory()
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            SELECT b.id, b.title, b.stock, COALESCE(t.minimum, 3)
            FROM books b LEFT JOIN inventory_thresholds t ON t.book_id = b.id ORDER BY b.title
            """);
        using var reader = command.ExecuteReader();
        var books = new List<InventoryBook>();
        while (reader.Read())
            books.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        return books;
    }

    public IReadOnlyList<StockMovement> Movements(int? bookId)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            SELECT b.title, m.delta, m.kind, m.ref_id, m.actor, m.occurred_at, m.note
            FROM stock_movements m JOIN books b ON b.id = m.book_id
            WHERE $book IS NULL OR m.book_id = $book ORDER BY m.occurred_at DESC, m.id DESC
            """, ("$book", (object?)bookId ?? DBNull.Value));
        using var reader = command.ExecuteReader();
        var movements = new List<StockMovement>();
        while (reader.Read())
            movements.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.GetString(4),
                ParseDate(reader.GetString(5)), OptionalText(reader, 6)));
        return movements;
    }

    public void SetMinimum(int bookId, int minimum)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            INSERT INTO inventory_thresholds(book_id, minimum)
            SELECT id, $minimum FROM books WHERE id = $book
            ON CONFLICT(book_id) DO UPDATE SET minimum = excluded.minimum
            """, ("$book", bookId), ("$minimum", minimum));
        if (command.ExecuteNonQuery() != 1)
            throw new ReceivingRuleException("Không tìm thấy đầu sách.");
    }

    public void Reconcile(int bookId, int expectedStock, int count, string reason, string actor, DateTimeOffset now)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var read = Command(connection, transaction, "SELECT stock FROM books WHERE id = $book", ("$book", bookId));
        if (read.ExecuteScalar() is not long stock)
            throw new ReceivingRuleException("Không tìm thấy đầu sách.");

        if (stock != expectedStock)
            throw new ReceivingRuleException("Tồn kho đã thay đổi từ khi mở trang. Hãy kiểm kê lại trước khi lưu.");

        var delta = count - expectedStock;
        if (delta != 0)
        {
            ChangeStock(connection, transaction, bookId, delta);
            RecordStockMovement(connection, transaction, bookId, delta, "Adjustment", null, actor, now, reason);
        }
        transaction.Commit();
    }

    private static void ChangeStock(SqliteConnection connection, SqliteTransaction transaction, int bookId, int delta)
    {
        // Reserve room for stock that may be restored if a confirmed order is cancelled.
        using var command = Command(connection, transaction, """
            UPDATE books SET stock = stock + $delta
            WHERE id = $book AND stock + $delta >= 0 AND stock + $delta + COALESCE((
                SELECT SUM(l.quantity) FROM order_lines l JOIN orders o ON o.id = l.order_id
                WHERE l.book_id = $book AND o.status IN ('Confirmed','Packing','Shipped','DeliveryFailed')
            ), 0) <= $maximum
            """, ("$book", bookId), ("$delta", delta), ("$maximum", int.MaxValue));
        if (command.ExecuteNonQuery() != 1)
            throw new ReceivingRuleException($"Sách mã {bookId} không tồn tại hoặc số lượng vượt giới hạn kho.");
    }

    internal static void RecordStockMovement(SqliteConnection connection, SqliteTransaction transaction, int bookId,
        int delta, string kind, long? reference, string actor, DateTimeOffset occurredAt, string? note = null)
    {
        using var command = Command(connection, transaction, """
            INSERT INTO stock_movements(book_id, delta, kind, ref_id, actor, occurred_at, note)
            VALUES ($book, $delta, $kind, $reference, $actor, $date, $note)
            """, ("$book", bookId), ("$delta", delta), ("$kind", kind), ("$reference", (object?)reference ?? DBNull.Value),
            ("$actor", actor), ("$date", FormatDate(occurredAt)), ("$note", (object?)note ?? DBNull.Value));
        command.ExecuteNonQuery();
    }

    private static string? OptionalText(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : reader.GetString(column);

    private static string FormatDate(DateTimeOffset date) => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}
