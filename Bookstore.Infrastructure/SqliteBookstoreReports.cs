using System.Globalization;
using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.Data.Sqlite;
using static Bookstore.Infrastructure.SqliteDatabase;

namespace Bookstore.Infrastructure;

public sealed class SqliteBookstoreReports(SqliteDatabase database) : IBookstoreReports
{
    public BookstoreReport Read(DateOnly from, DateOnly through, DateOnly today)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var fromDate = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var throughDate = through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var todayDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var headline = Command(connection, transaction, """
            SELECT
                COALESCE(SUM(CASE WHEN status = 'Completed' AND date(completed_at, '+7 hours') = $today THEN total ELSE 0 END), 0),
                COUNT(CASE WHEN date(created_at, '+7 hours') = $today THEN 1 END),
                COUNT(CASE WHEN status = 'Pending' THEN 1 END)
            FROM orders
            """, ("$today", todayDate));
        long revenueToday;
        int newOrders;
        int pending;
        using (var reader = headline.ExecuteReader())
        {
            reader.Read();
            revenueToday = reader.GetInt64(0);
            newOrders = reader.GetInt32(1);
            pending = reader.GetInt32(2);
        }

        var revenue = Query(connection, transaction, """
            SELECT date(completed_at, '+7 hours'), COUNT(*), SUM(total)
            FROM orders WHERE status = 'Completed' AND date(completed_at, '+7 hours') BETWEEN $from AND $through
            GROUP BY 1 ORDER BY 1
            """, reader => new DailyRevenue(reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2)),
            ("$from", fromDate), ("$through", throughDate));

        var orders = Query(connection, transaction, """
            SELECT status, COUNT(*) FROM orders
            WHERE date(created_at, '+7 hours') BETWEEN $from AND $through
            GROUP BY status ORDER BY status
            """, reader => new OrderCount(reader.GetString(0), reader.GetInt32(1)),
            ("$from", fromDate), ("$through", throughDate));

        var topBooks = Query(connection, transaction, """
            SELECT b.title, SUM(l.quantity) FROM order_lines l JOIN orders o ON o.id = l.order_id
            JOIN books b ON b.id = l.book_id
            WHERE o.status = 'Completed' AND date(o.completed_at, '+7 hours') BETWEEN $from AND $through
            GROUP BY b.id, b.title ORDER BY 2 DESC, b.title LIMIT 10
            """, reader => new TopBook(reader.GetString(0), reader.GetInt64(1)),
            ("$from", fromDate), ("$through", throughDate));

        var inventory = Query(connection, transaction, """
            SELECT b.id, b.title, b.stock, COALESCE(t.minimum, 3)
            FROM books b LEFT JOIN inventory_thresholds t ON t.book_id = b.id
            ORDER BY b.stock, b.title
            """, reader => new InventoryBook(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));

        using var receiving = Command(connection, transaction, """
            SELECT COALESCE(SUM(l.received_quantity), 0), COALESCE(SUM(l.received_quantity * l.unit_cost), 0)
            FROM purchase_orders p JOIN purchase_order_lines l ON l.purchase_id = p.id
            WHERE p.status = 'Received' AND date(p.received_at, '+7 hours') BETWEEN $from AND $through
            """, ("$from", fromDate), ("$through", throughDate));
        using var receipts = receiving.ExecuteReader();
        receipts.Read();
        return new(revenueToday, newOrders, pending, revenue, orders, topBooks, inventory,
            receipts.GetInt64(0), receipts.GetInt64(1));
    }

    private static List<T> Query<T>(SqliteConnection connection, SqliteTransaction transaction, string sql,
        Func<SqliteDataReader, T> read, params (string Name, object Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        using var reader = command.ExecuteReader();
        var results = new List<T>();
        while (reader.Read())
            results.Add(read(reader));
        return results;
    }
}
