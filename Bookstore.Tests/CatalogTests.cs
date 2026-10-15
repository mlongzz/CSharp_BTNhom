using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Bookstore.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("bookstore-catalog-test-").FullName;
    private string DatabasePath => Path.Combine(directory, "catalog.db");

    private CatalogService OpenCatalog()
    {
        var database = new SqliteDatabase(DatabasePath);
        database.Initialize();
        return new(new SqliteCatalogRepository(database), TimeProvider.System);
    }

    [Fact]
    public void SearchMatchesAuthorAndIsbnAndExcludesHiddenBooks()
    {
        var catalog = OpenCatalog();
        var filter = new CatalogFilter("tÔ hOÀI", null, null, null, null, null, null, null, 1);
        Assert.Equal(1, Assert.Single(catalog.Search(filter).Books).Id);

        var book = Assert.Single(catalog.Search(filter with { Query = "DEMO-0001" }).Books);
        var draft = new CatalogBookDraft(book.Title, book.Isbn, book.Price, false, book.CategoryId,
            book.AuthorId, book.PublisherId, book.Description, book.CoverUrl, book.Pages, book.Year);
        catalog.SaveBook(book.Id, draft);
        Assert.Empty(catalog.Search(filter).Books);
        Assert.Null(catalog.Details(book.Id, null));
    }

    [Fact]
    public void ReviewsRequireCompletedOwnedOrderAndCannotRepeat()
    {
        var catalog = OpenCatalog();
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO orders(id, customer, address, status, total)
                    VALUES (1, 'customer', 'Địa chỉ', 'Completed', 65000),
                           (2, 'customer', 'Địa chỉ', 'Pending', 65000);
                INSERT INTO order_lines(order_id, book_id, quantity, price) VALUES (1, 1, 1, 65000), (2, 1, 1, 65000);
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<CatalogException>(() => catalog.Review("other", 1, 1, 5, "Hay"));
        Assert.Throws<CatalogException>(() => catalog.Review("customer", 1, 2, 5, "Hay"));
        Assert.Throws<CatalogException>(() => catalog.Review("customer", 1, 1, 6, "Hay"));
        Assert.Equal(1L, Assert.Single(catalog.Details(1, "customer")!.EligibleOrders));
        catalog.Review("customer", 1, 1, 5, "Hay");
        Assert.Throws<CatalogException>(() => catalog.Review("customer", 1, 1, 5, "Lặp"));
        var details = catalog.Details(1, "customer")!;
        Assert.Empty(details.EligibleOrders);
        Assert.Equal("Hay", Assert.Single(details.Reviews).Comment);
    }

    [Fact]
    public void VoucherCodeIsUniqueIgnoringCase()
    {
        var catalog = OpenCatalog();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var draft = new VoucherDraft("SALE10", "Percent", 10, 50000, start, start.AddDays(1), 2);
        catalog.SaveVoucher(null, draft);

        Assert.Throws<CatalogException>(() => catalog.SaveVoucher(null, draft with { Code = "sale10" }));
        Assert.Throws<CatalogException>(() => catalog.SaveVoucher(null, draft with { Code = "ANOTHER", Value = 101 }));
        var saved = Assert.Single(catalog.Vouchers());
        Assert.Equal(draft, saved.Draft);
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
