using Microsoft.Data.Sqlite;
using static Bookstore.Infrastructure.SqliteDatabase;

namespace Bookstore.Infrastructure;

public sealed partial class SqliteOrderRepository
{
    internal static void Migrate(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = Command(connection, transaction, """
            DROP INDEX idx_orders_customer;
            DROP INDEX idx_order_events_order;
            ALTER TABLE order_lines RENAME TO order_lines_v2;
            ALTER TABLE order_events RENAME TO order_events_v2;
            ALTER TABLE orders RENAME TO orders_v2;
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY, customer TEXT NOT NULL, address TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('Pending','Confirmed','Packing','Shipped','Completed','Cancelled')),
                total INTEGER NOT NULL CHECK(total >= 0),
                created_at TEXT, completed_at TEXT, carrier TEXT, tracking_code TEXT, rejection_reason TEXT,
                payment_method TEXT NOT NULL DEFAULT 'Cod', payment_status TEXT NOT NULL DEFAULT 'Unpaid',
                payment_reference TEXT UNIQUE, discount INTEGER NOT NULL DEFAULT 0, shipping_fee INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE order_lines (
                order_id INTEGER NOT NULL REFERENCES orders(id),
                book_id INTEGER NOT NULL REFERENCES books(id),
                quantity INTEGER NOT NULL CHECK(quantity > 0),
                price INTEGER NOT NULL CHECK(price > 0),
                PRIMARY KEY(order_id, book_id)
            );
            CREATE TABLE order_events (
                id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES orders(id),
                action TEXT NOT NULL, actor TEXT NOT NULL, occurred_at TEXT NOT NULL
            );
            CREATE TABLE rejected_payments (reference TEXT PRIMARY KEY);
            CREATE TABLE return_requests (
                id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL, book_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL CHECK(quantity > 0), reason TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('Pending','Approved','Rejected','Completed')),
                outcome TEXT NOT NULL CHECK(outcome IN ('Refund','Exchange')),
                requested_at TEXT NOT NULL, completed_at TEXT, refund_amount INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(order_id, book_id) REFERENCES order_lines(order_id, book_id),
                UNIQUE(order_id, book_id)
            );
            INSERT INTO orders(id,customer,address,status,total,created_at)
                SELECT id,customer,address,status,total,(
                    SELECT occurred_at FROM order_events_v2 WHERE order_id=orders_v2.id AND action='Placed' ORDER BY id LIMIT 1
                ) FROM orders_v2;
            INSERT INTO order_lines SELECT * FROM order_lines_v2;
            INSERT INTO order_events SELECT * FROM order_events_v2;
            DROP TABLE order_lines_v2;
            DROP TABLE order_events_v2;
            DROP TABLE orders_v2;
            CREATE INDEX idx_orders_customer ON orders(customer);
            CREATE INDEX idx_order_events_order ON order_events(order_id);
            CREATE INDEX idx_order_lines_book ON order_lines(book_id);
            """);
        command.ExecuteNonQuery();
    }

    internal static void MigrateDeliveryReturns(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = Command(connection, transaction, """
            CREATE TABLE orders_v4 (
                id INTEGER PRIMARY KEY, customer TEXT NOT NULL, address TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('Pending','Confirmed','Packing','Shipped','Completed',
                    'Cancelled','DeliveryFailed','ReturnedToWarehouse')),
                total INTEGER NOT NULL CHECK(total >= 0),
                created_at TEXT, completed_at TEXT, carrier TEXT, tracking_code TEXT, rejection_reason TEXT,
                payment_method TEXT NOT NULL DEFAULT 'Cod', payment_status TEXT NOT NULL DEFAULT 'Unpaid',
                payment_reference TEXT UNIQUE, discount INTEGER NOT NULL DEFAULT 0, shipping_fee INTEGER NOT NULL DEFAULT 0
            );
            INSERT INTO orders_v4(id,customer,address,status,total,created_at,completed_at,carrier,tracking_code,
                rejection_reason,payment_method,payment_status,payment_reference,discount,shipping_fee)
            SELECT id,customer,address,status,total,created_at,completed_at,carrier,tracking_code,
                rejection_reason,payment_method,payment_status,payment_reference,discount,shipping_fee FROM orders;
            DROP TABLE orders;
            ALTER TABLE orders_v4 RENAME TO orders;
            CREATE INDEX idx_orders_customer ON orders(customer);
            """);
        command.ExecuteNonQuery();
    }
}
