using Microsoft.Data.Sqlite;

namespace Bookstore.Infrastructure;

public sealed class SqliteDatabase(string databasePath)
{
    private const int SchemaVersion = 3;

    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Initialize()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var versionQuery = Command(connection, transaction, "PRAGMA user_version");
        var version = (long)versionQuery.ExecuteScalar()!;
        if (version == SchemaVersion)
            return;
        if (version is < 0 or > SchemaVersion)
            throw new InvalidOperationException($"Unsupported Bookstore database schema: {version}.");

        if (version == 0)
            UpgradeOrders(connection, transaction);

        if (version < 2)
            UpgradeReceipts(connection, transaction);

        SqliteOrderRepository.Migrate(connection, transaction);
        SqliteCustomerAccounts.Migrate(connection, transaction);
        SqliteCatalogRepository.Migrate(connection, transaction);
        SqliteReceiptRepository.Migrate(connection, transaction);

        using var setVersion = Command(connection, transaction, $"PRAGMA user_version = {SchemaVersion}");
        setVersion.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void UpgradeReceipts(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var receiving = Command(connection, transaction, """
            CREATE TABLE receipts (
                id INTEGER PRIMARY KEY,
                supplier TEXT NOT NULL CHECK(length(supplier) BETWEEN 1 AND 120),
                staff TEXT NOT NULL,
                received_at TEXT NOT NULL
            );
            CREATE TABLE receipt_lines (
                receipt_id INTEGER NOT NULL REFERENCES receipts(id),
                book_id INTEGER NOT NULL REFERENCES books(id),
                quantity INTEGER NOT NULL CHECK(quantity > 0),
                PRIMARY KEY(receipt_id, book_id)
            );
            """);
        receiving.ExecuteNonQuery();
    }

    private static void UpgradeOrders(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var legacyQuery = Command(connection, transaction,
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'orders'");
        var legacy = legacyQuery.ExecuteScalar() is not null;
        if (legacy)
        {
            using var rename = Command(connection, transaction, """
                ALTER TABLE order_lines RENAME TO order_lines_legacy;
                ALTER TABLE orders RENAME TO orders_legacy;
                """);
            rename.ExecuteNonQuery();
        }

        using var schema = Command(connection, transaction, """
            CREATE TABLE IF NOT EXISTS customers (
                username TEXT PRIMARY KEY, password_hash TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS books (
                id INTEGER PRIMARY KEY, title TEXT NOT NULL,
                price INTEGER NOT NULL CHECK(price > 0),
                stock INTEGER NOT NULL CHECK(stock >= 0)
            );
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY, customer TEXT NOT NULL, address TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('Pending', 'Confirmed', 'Cancelled')),
                total INTEGER NOT NULL CHECK(total > 0)
            );
            CREATE TABLE order_lines (
                order_id INTEGER NOT NULL REFERENCES orders(id),
                book_id INTEGER NOT NULL REFERENCES books(id),
                quantity INTEGER NOT NULL CHECK(quantity > 0),
                price INTEGER NOT NULL CHECK(price > 0),
                PRIMARY KEY(order_id, book_id)
            );
            CREATE TABLE order_events (
                id INTEGER PRIMARY KEY,
                order_id INTEGER NOT NULL REFERENCES orders(id),
                action TEXT NOT NULL CHECK(action IN ('Placed', 'Confirmed', 'Cancelled')),
                actor TEXT NOT NULL, occurred_at TEXT NOT NULL
            );
            CREATE INDEX idx_orders_customer ON orders(customer);
            CREATE INDEX idx_order_events_order ON order_events(order_id);
            """);
        schema.ExecuteNonQuery();

        if (legacy)
        {
            using var copy = Command(connection, transaction, """
                INSERT INTO orders(id, customer, address, status, total)
                    SELECT id, customer, address, status, total FROM orders_legacy;
                INSERT INTO order_lines(order_id, book_id, quantity, price)
                    SELECT order_id, book_id, quantity, price FROM order_lines_legacy;
                DROP TABLE order_lines_legacy;
                DROP TABLE orders_legacy;
                """);
            copy.ExecuteNonQuery();
        }
        else
        {
            using var seed = Command(connection, transaction, """
                INSERT OR IGNORE INTO books VALUES
                    (1, 'Dế Mèn phiêu lưu ký', 65000, 5),
                    (2, 'Tuổi thơ dữ dội', 120000, 8),
                    (3, 'Cho tôi xin một vé đi tuổi thơ', 85000, 4);
                """);
            seed.ExecuteNonQuery();
        }
    }

    internal static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }
}

