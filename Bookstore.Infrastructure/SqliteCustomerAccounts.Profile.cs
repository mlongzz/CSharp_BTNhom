using Bookstore.Domain;
using Microsoft.Data.Sqlite;
using static Bookstore.Infrastructure.SqliteDatabase;

namespace Bookstore.Infrastructure;

public sealed partial class SqliteCustomerAccounts
{
    public IReadOnlyList<ShippingAddress> Addresses(string username)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            SELECT id, recipient, phone, province, district, ward, street, is_default
            FROM shipping_addresses WHERE username = $username ORDER BY is_default DESC, id
            """, ("$username", username));
        using var reader = command.ExecuteReader();
        var addresses = new List<ShippingAddress>();
        while (reader.Read())
            addresses.Add(ReadAddress(reader));
        return addresses;
    }

    public ShippingAddress? Address(string username, long id)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            SELECT id, recipient, phone, province, district, ward, street, is_default
            FROM shipping_addresses WHERE username = $username AND id = $id
            """, ("$username", username), ("$id", id));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAddress(reader) : null;
    }

    public bool SaveAddress(string username, ShippingAddress address)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var lookup = Command(connection, transaction, """
            SELECT is_default FROM shipping_addresses WHERE username = $username AND id = $id
            """, ("$username", username), ("$id", address.Id));
        var prior = lookup.ExecuteScalar();
        if (address.Id != 0 && prior is null)
            return false;

        using var count = Command(connection, transaction, """
            SELECT COUNT(*) FROM shipping_addresses WHERE username = $username
            """, ("$username", username));
        var isDefault = address.IsDefault || prior is 1L || (long)count.ExecuteScalar()! == 0;
        if (isDefault)
        {
            using var clear = Command(connection, transaction, """
                UPDATE shipping_addresses SET is_default = 0 WHERE username = $username AND is_default = 1
                """, ("$username", username));
            clear.ExecuteNonQuery();
        }

        var sql = address.Id == 0 ? """
            INSERT INTO shipping_addresses(username, recipient, phone, province, district, ward, street, is_default)
            VALUES ($username, $recipient, $phone, $province, $district, $ward, $street, $default)
            """ : """
            UPDATE shipping_addresses SET recipient = $recipient, phone = $phone, province = $province,
                district = $district, ward = $ward, street = $street, is_default = $default
            WHERE username = $username AND id = $id
            """;
        using var save = Command(connection, transaction, sql, ("$username", username), ("$id", address.Id),
            ("$recipient", address.Recipient), ("$phone", address.Phone), ("$province", address.Province),
            ("$district", address.District), ("$ward", address.Ward), ("$street", address.Street), ("$default", isDefault));
        save.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public bool DeleteAddress(string username, long id)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var delete = Command(connection, transaction, """
            DELETE FROM shipping_addresses WHERE username = $username AND id = $id
            """, ("$username", username), ("$id", id));
        if (delete.ExecuteNonQuery() != 1)
            return false;

        using var promote = Command(connection, transaction, """
            UPDATE shipping_addresses SET is_default = 1
            WHERE id = (SELECT MIN(id) FROM shipping_addresses WHERE username = $username)
              AND NOT EXISTS (SELECT 1 FROM shipping_addresses WHERE username = $username AND is_default = 1)
            """, ("$username", username));
        promote.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public void QueueEmail(string username, string subject, string message)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        QueueEmail(connection, transaction, username, subject, message);
        transaction.Commit();
    }

    internal static void QueueEmail(SqliteConnection connection, SqliteTransaction transaction,
        string username, string subject, string message)
    {
        using var command = Command(connection, transaction, """
            INSERT INTO mail_outbox(username, recipient_email, subject, message, created_at)
            SELECT username, email, $subject, $message, unixepoch()
            FROM customers WHERE username = $username AND email IS NOT NULL
            """, ("$username", username), ("$subject", subject), ("$message", message));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<AccountMail> Mail(string username)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            SELECT subject, message, created_at FROM mail_outbox WHERE username = $username ORDER BY id DESC LIMIT 20
            """, ("$username", username));
        using var reader = command.ExecuteReader();
        var messages = new List<AccountMail>();
        while (reader.Read())
            messages.Add(new(reader.GetString(0), reader.GetString(1), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2))));
        return messages;
    }

    private static ShippingAddress ReadAddress(SqliteDataReader reader) => new(reader.GetInt64(0),
        reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.GetString(6), reader.GetBoolean(7));
}

