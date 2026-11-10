using Bookstore.Application;
using Microsoft.Data.Sqlite;
using static Bookstore.Infrastructure.SqliteDatabase;

namespace Bookstore.Infrastructure;

public sealed partial class SqliteCustomerAccounts
{
    internal static void MigrateMailDelivery(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = Command(connection, transaction, """
            CREATE TABLE IF NOT EXISTS mail_deliveries (
                mail_id INTEGER PRIMARY KEY REFERENCES mail_outbox(id) ON DELETE CASCADE,
                lease TEXT,
                retry_at INTEGER NOT NULL,
                delivered_at INTEGER
            );
            """);
        command.ExecuteNonQuery();
    }

    public OutboxMessage? Claim(DateTimeOffset now)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var lease = Guid.NewGuid().ToString("N");
        using var claim = Command(connection, transaction, """
            INSERT INTO mail_deliveries(mail_id, lease, retry_at)
            SELECT m.id, $lease, $until
            FROM mail_outbox m
            LEFT JOIN mail_deliveries d ON d.mail_id = m.id
            WHERE d.mail_id IS NULL OR (d.delivered_at IS NULL AND d.retry_at <= $now)
            ORDER BY m.id LIMIT 1
            ON CONFLICT(mail_id) DO UPDATE SET lease = excluded.lease, retry_at = excluded.retry_at
            RETURNING mail_id
            """, ("$lease", lease), ("$until", now.AddMinutes(5).ToUnixTimeSeconds()), ("$now", now.ToUnixTimeSeconds()));
        if (claim.ExecuteScalar() is not long id)
            return null;

        using var read = Command(connection, transaction, """
            SELECT recipient_email, subject, message FROM mail_outbox WHERE id = $id
            """, ("$id", id));
        OutboxMessage message;
        using (var reader = read.ExecuteReader())
        {
            if (!reader.Read())
                return null;
            message = new(id, reader.GetString(0), reader.GetString(1), reader.GetString(2), lease);
        }

        transaction.Commit();
        return message;
    }

    public void Finish(OutboxMessage message, DateTimeOffset now, bool delivered)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            UPDATE mail_deliveries
            SET lease = NULL, delivered_at = $delivered, retry_at = $retry
            WHERE mail_id = $id AND lease = $lease
            """, ("$delivered", delivered ? now.ToUnixTimeSeconds() : DBNull.Value),
            ("$retry", now.AddMinutes(1).ToUnixTimeSeconds()), ("$id", message.Id), ("$lease", message.Lease));
        command.ExecuteNonQuery();
    }
}

