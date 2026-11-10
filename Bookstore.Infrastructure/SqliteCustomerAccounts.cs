using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.Data.Sqlite;
using static Bookstore.Infrastructure.SqliteDatabase;

namespace Bookstore.Infrastructure;

public sealed partial class SqliteCustomerAccounts(SqliteDatabase database) : ICustomerAccounts, IMailDeliveryQueue
{
    private const string Columns = "username, email, display_name, phone, role, locked, last_login";

    internal static void Migrate(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var migration = Command(connection, transaction, """
            ALTER TABLE customers ADD COLUMN email TEXT;
            ALTER TABLE customers ADD COLUMN display_name TEXT NOT NULL DEFAULT '';
            ALTER TABLE customers ADD COLUMN phone TEXT NOT NULL DEFAULT '';
            ALTER TABLE customers ADD COLUMN role TEXT NOT NULL DEFAULT 'Customer'
                CHECK(role IN ('Customer', 'Sales', 'Warehouse', 'Admin'));
            ALTER TABLE customers ADD COLUMN locked INTEGER NOT NULL DEFAULT 0 CHECK(locked IN (0, 1));
            ALTER TABLE customers ADD COLUMN failed_attempts INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE customers ADD COLUMN locked_until INTEGER;
            ALTER TABLE customers ADD COLUMN last_login INTEGER;
            CREATE UNIQUE INDEX idx_customers_email ON customers(email COLLATE NOCASE);
            CREATE TABLE auth_sessions (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL REFERENCES customers(username) ON DELETE CASCADE,
                expires_at INTEGER NOT NULL
            );
            CREATE INDEX idx_auth_sessions_username ON auth_sessions(username);
            CREATE TABLE shipping_addresses (
                id INTEGER PRIMARY KEY,
                username TEXT NOT NULL REFERENCES customers(username) ON DELETE CASCADE,
                recipient TEXT NOT NULL,
                phone TEXT NOT NULL,
                province TEXT NOT NULL,
                district TEXT NOT NULL,
                ward TEXT NOT NULL,
                street TEXT NOT NULL,
                is_default INTEGER NOT NULL DEFAULT 0 CHECK(is_default IN (0, 1))
            );
            CREATE INDEX idx_shipping_addresses_username ON shipping_addresses(username);
            CREATE UNIQUE INDEX idx_shipping_addresses_default ON shipping_addresses(username) WHERE is_default = 1;
            CREATE TABLE mail_outbox (
                id INTEGER PRIMARY KEY,
                username TEXT NOT NULL REFERENCES customers(username) ON DELETE CASCADE,
                recipient_email TEXT NOT NULL,
                subject TEXT NOT NULL,
                message TEXT NOT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE INDEX idx_mail_outbox_username ON mail_outbox(username);
            """);
        migration.ExecuteNonQuery();
        MigrateMailDelivery(connection, transaction);
    }

    public bool Create(string username, string email, string name, string passwordHash, string role, bool sendWelcome)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = Command(connection, transaction, """
            INSERT INTO customers(username, email, display_name, password_hash, role)
            VALUES ($username, $email, $name, $hash, $role)
            ON CONFLICT DO NOTHING
            """, ("$username", username), ("$email", email), ("$name", name), ("$hash", passwordHash), ("$role", role));
        if (command.ExecuteNonQuery() != 1)
            return false;

        if (sendWelcome)
            QueueEmail(connection, transaction, username, "Xác nhận đăng ký", "Tài khoản nhà sách của bạn đã được tạo.");
        transaction.Commit();
        return true;
    }

    public AccountCredentials? Credentials(string identity)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, $"""
            SELECT {Columns}, password_hash, locked_until FROM customers
            WHERE username = $identity OR email = $identity COLLATE NOCASE
            """, ("$identity", identity));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new(ReadAccount(reader), reader.GetString(7), Timestamp(reader, 8))
            : null;
    }

    public string? PasswordHash(string username)
    {
        using var connection = database.Open();
        using var command = Command(connection, null,
            "SELECT password_hash FROM customers WHERE username = $username", ("$username", username));
        return command.ExecuteScalar() as string;
    }

    public void RecordFailedAttempt(string username, DateTimeOffset now)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            UPDATE customers SET
                locked_until = CASE WHEN locked_until IS NULL AND failed_attempts >= 4 THEN $until ELSE NULL END,
                failed_attempts = CASE WHEN locked_until IS NULL THEN failed_attempts + 1 ELSE 1 END
            WHERE username = $username AND locked = 0 AND (locked_until IS NULL OR locked_until <= $now)
            """, ("$username", username), ("$now", now.ToUnixTimeSeconds()),
            ("$until", now.AddMinutes(5).ToUnixTimeSeconds()));
        command.ExecuteNonQuery();
    }

    public bool StartSession(string username, string passwordHash, string sessionId,
        DateTimeOffset now, DateTimeOffset expiresAt)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var update = Command(connection, transaction, """
            UPDATE customers SET last_login = $now, failed_attempts = 0, locked_until = NULL
            WHERE username = $username AND password_hash = $hash AND locked = 0
                AND (locked_until IS NULL OR locked_until <= $now)
            """, ("$username", username), ("$hash", passwordHash), ("$now", now.ToUnixTimeSeconds()));
        if (update.ExecuteNonQuery() != 1)
            return false;

        using var create = Command(connection, transaction, """
            DELETE FROM auth_sessions WHERE username = $username AND expires_at <= $now;
            INSERT INTO auth_sessions(id, username, expires_at) VALUES ($id, $username, $expires);
            """, ("$id", sessionId), ("$username", username), ("$now", now.ToUnixTimeSeconds()),
            ("$expires", expiresAt.ToUnixTimeSeconds()));
        create.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public Account? SessionAccount(string username, string sessionId, DateTimeOffset now)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, $"""
            SELECT {Columns} FROM customers WHERE username = $username AND locked = 0
              AND EXISTS (SELECT 1 FROM auth_sessions
                  WHERE id = $session AND username = $username AND expires_at > $now)
            """, ("$username", username), ("$session", sessionId), ("$now", now.ToUnixTimeSeconds()));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAccount(reader) : null;
    }

    public void EndSession(string username, string sessionId)
    {
        using var connection = database.Open();
        using var command = Command(connection, null,
            "DELETE FROM auth_sessions WHERE id = $id AND username = $username",
            ("$id", sessionId), ("$username", username));
        command.ExecuteNonQuery();
    }

    public Account? Find(string username)
    {
        using var connection = database.Open();
        using var command = Command(connection, null,
            $"SELECT {Columns} FROM customers WHERE username = $username", ("$username", username));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAccount(reader) : null;
    }

    public IReadOnlyList<Account> List()
    {
        using var connection = database.Open();
        using var command = Command(connection, null, $"SELECT {Columns} FROM customers ORDER BY role, username");
        using var reader = command.ExecuteReader();
        var accounts = new List<Account>();
        while (reader.Read())
            accounts.Add(ReadAccount(reader));
        return accounts;
    }

    public bool UpdateProfile(string username, string email, string name, string phone)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            UPDATE OR IGNORE customers SET email = $email, display_name = $name, phone = $phone
            WHERE username = $username AND role = 'Customer' AND locked = 0
            """, ("$username", username), ("$email", email), ("$name", name), ("$phone", phone));
        return command.ExecuteNonQuery() == 1;
    }

    public bool UpdateAccess(string username, string role, bool locked)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var update = Command(connection, transaction, """
            UPDATE customers SET role = $role, locked = $locked
            WHERE username = $username AND (role <> $role OR locked <> $locked)
                AND (role <> 'Admin' OR locked = 1 OR ($role = 'Admin' AND $locked = 0)
                    OR EXISTS (
                        SELECT 1 FROM customers WHERE username <> $username AND role = 'Admin' AND locked = 0
                    ))
            """, ("$username", username), ("$role", role), ("$locked", locked));
        if (update.ExecuteNonQuery() != 1)
            return false;

        using var revoke = Command(connection, transaction, "DELETE FROM auth_sessions WHERE username = $username",
            ("$username", username));
        revoke.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    private static Account ReadAccount(SqliteDataReader reader) => new(reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetBoolean(5), Timestamp(reader, 6));

    private static DateTimeOffset? Timestamp(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(ordinal));
}
