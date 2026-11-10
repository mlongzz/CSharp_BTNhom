using System.Globalization;
using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.Data.Sqlite;
using static Bookstore.Infrastructure.SqliteDatabase;

namespace Bookstore.Infrastructure;

public sealed class SqliteCatalogRepository(SqliteDatabase database) : ICatalogRepository
{
    private const int PageSize = 12;
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private const string Rating = "COALESCE((SELECT AVG(stars) FROM reviews WHERE book_id = b.id), 0)";
    private const string BookSelect = """
        SELECT b.id, b.title, b.isbn, b.price, b.stock, b.active, b.category_id, b.author_id, b.publisher_id,
            c.name, a.name, p.name, b.description, b.cover_url, b.pages, b.year,
            COALESCE((SELECT AVG(stars) FROM reviews WHERE book_id = b.id), 0),
            (SELECT COUNT(*) FROM reviews WHERE book_id = b.id)
        """;
    private const string BookSource = """
        FROM books b
        LEFT JOIN categories c ON c.id = b.category_id
        LEFT JOIN authors a ON a.id = b.author_id
        LEFT JOIN publishers p ON p.id = b.publisher_id
        """;
    private const string TextFilter = """
        ($q IS NULL OR contains_ci(b.title, $q) OR contains_ci(b.isbn, $q) OR contains_ci(a.name, $q))
        """;

    internal static void Migrate(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = Command(connection, transaction, """
            CREATE TABLE categories (
                id INTEGER PRIMARY KEY, name TEXT NOT NULL, description TEXT,
                parent_id INTEGER REFERENCES categories(id) ON DELETE RESTRICT
            );
            CREATE TABLE authors (id INTEGER PRIMARY KEY, name TEXT NOT NULL, description TEXT);
            CREATE TABLE publishers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, description TEXT);
            ALTER TABLE books ADD COLUMN isbn TEXT NOT NULL DEFAULT '';
            ALTER TABLE books ADD COLUMN active INTEGER NOT NULL DEFAULT 1 CHECK(active IN (0, 1));
            ALTER TABLE books ADD COLUMN category_id INTEGER REFERENCES categories(id) ON DELETE RESTRICT;
            ALTER TABLE books ADD COLUMN author_id INTEGER REFERENCES authors(id) ON DELETE RESTRICT;
            ALTER TABLE books ADD COLUMN publisher_id INTEGER REFERENCES publishers(id) ON DELETE RESTRICT;
            ALTER TABLE books ADD COLUMN description TEXT;
            ALTER TABLE books ADD COLUMN cover_url TEXT;
            ALTER TABLE books ADD COLUMN pages INTEGER;
            ALTER TABLE books ADD COLUMN year INTEGER;
            INSERT INTO categories(id, name) VALUES (1, 'Văn học'), (2, 'Thiếu nhi');
            INSERT INTO authors(id, name) VALUES (1, 'Tô Hoài'), (2, 'Phùng Quán'), (3, 'Nguyễn Nhật Ánh');
            INSERT INTO publishers(id, name) VALUES (1, 'Nhà xuất bản mẫu');
            UPDATE books SET isbn = printf('DEMO-%04d', id);
            UPDATE books SET author_id = 1, category_id = 2, publisher_id = 1 WHERE id = 1;
            UPDATE books SET author_id = 2, category_id = 1, publisher_id = 1 WHERE id = 2;
            UPDATE books SET author_id = 3, category_id = 2, publisher_id = 1 WHERE id = 3;
            CREATE UNIQUE INDEX idx_books_isbn ON books(isbn COLLATE NOCASE);
            CREATE TABLE reviews (
                customer TEXT NOT NULL, book_id INTEGER NOT NULL,
                order_id INTEGER NOT NULL, stars INTEGER NOT NULL CHECK(stars BETWEEN 1 AND 5),
                comment TEXT NOT NULL, created_at TEXT NOT NULL,
                PRIMARY KEY(customer, book_id, order_id),
                FOREIGN KEY(order_id, book_id) REFERENCES order_lines(order_id, book_id) ON DELETE RESTRICT
            );
            CREATE INDEX idx_reviews_book ON reviews(book_id);
            CREATE TABLE vouchers (
                id INTEGER PRIMARY KEY, code TEXT NOT NULL COLLATE NOCASE UNIQUE,
                discount_type TEXT NOT NULL CHECK(discount_type IN ('Percent', 'Amount')),
                value INTEGER NOT NULL CHECK(value > 0 AND (discount_type = 'Amount' OR value <= 100)),
                minimum_total INTEGER NOT NULL CHECK(minimum_total >= 0),
                starts_at TEXT NOT NULL, ends_at TEXT NOT NULL CHECK(ends_at > starts_at),
                max_uses INTEGER NOT NULL CHECK(max_uses > 0),
                used_count INTEGER NOT NULL DEFAULT 0 CHECK(used_count BETWEEN 0 AND max_uses)
            );
            """);
        command.ExecuteNonQuery();
    }

    public CatalogPage Search(CatalogFilter filter)
    {
        using var connection = OpenForSearch();
        using var transaction = connection.BeginTransaction(deferred: true);
        var condition = $"""
            WHERE b.active = 1 AND {TextFilter}
                AND ($category IS NULL OR b.category_id = $category)
                AND ($author IS NULL OR b.author_id = $author)
                AND ($publisher IS NULL OR b.publisher_id = $publisher)
                AND ($min IS NULL OR b.price >= $min)
                AND ($max IS NULL OR b.price <= $max)
                AND ($rating IS NULL OR {Rating} >= $rating)
            """;
        (string, object)[] parameters =
        [
            ("$q", Db(filter.Query)), ("$category", Db(filter.Category)), ("$author", Db(filter.Author)),
            ("$publisher", Db(filter.Publisher)), ("$min", Db(filter.MinimumPrice)), ("$max", Db(filter.MaximumPrice)),
            ("$rating", Db(filter.Rating))
        ];
        using var count = Command(connection, transaction, $"SELECT COUNT(*) {BookSource} {condition}", parameters);
        var total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        var lastPage = total == 0 ? 1 : (total - 1) / PageSize + 1;
        var page = Math.Clamp(filter.Page, 1, lastPage);
        var sort = filter.Sort switch
        {
            "price-asc" => "b.price, b.id DESC",
            "price-desc" => "b.price DESC, b.id DESC",
            "bestselling" => """
                (SELECT COALESCE(SUM(l.quantity), 0) FROM order_lines l JOIN orders o ON o.id=l.order_id
                 WHERE l.book_id=b.id AND o.status='Completed') DESC, b.id DESC
                """,
            _ => "b.id DESC"
        };
        using var books = Command(connection, transaction, $"""
            {BookSelect} {BookSource} {condition}
            ORDER BY {sort} LIMIT $limit OFFSET $offset
            """, [.. parameters, ("$limit", PageSize), ("$offset", (page - 1) * PageSize)]);
        return new(ReadBooks(books), filter with { Page = page }, ReadReferences(connection, transaction), total, PageSize);
    }

    public BookDetails? Details(int id, string? customer)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var find = Command(connection, transaction, $"{BookSelect} {BookSource} WHERE b.active=1 AND b.id=$id",
            ("$id", id));
        var book = ReadBooks(find).SingleOrDefault();
        if (book is null)
            return null;

        using var reviewQuery = Command(connection, transaction, """
            SELECT customer, stars, comment, created_at FROM reviews WHERE book_id=$id ORDER BY created_at DESC
            """, ("$id", id));
        var reviews = new List<CatalogReview>();
        using (var reader = reviewQuery.ExecuteReader())
        {
            while (reader.Read())
                reviews.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), ParseDate(reader.GetString(3))));
        }

        using var relatedQuery = Command(connection, transaction, $"""
            {BookSelect} {BookSource}
            WHERE b.active=1 AND b.id<>$id AND (b.category_id=$category OR b.author_id=$author)
            ORDER BY b.id DESC LIMIT 4
            """, ("$id", id), ("$category", Db(book.CategoryId)), ("$author", Db(book.AuthorId)));
        var related = ReadBooks(relatedQuery);
        var eligibleOrders = new List<long>();
        if (customer is not null)
        {
            using var eligible = Command(connection, transaction, """
                SELECT o.id FROM orders o JOIN order_lines l ON l.order_id=o.id
                WHERE o.customer=$customer AND o.status='Completed' AND l.book_id=$id
                    AND NOT EXISTS (SELECT 1 FROM reviews r
                        WHERE r.customer=o.customer AND r.order_id=o.id AND r.book_id=l.book_id)
                ORDER BY o.id DESC
                """, ("$id", id), ("$customer", customer));
            using var reader = eligible.ExecuteReader();
            while (reader.Read())
                eligibleOrders.Add(reader.GetInt64(0));
        }

        return new(book, reviews, related, eligibleOrders);
    }

    public CatalogBook? FindBook(int id)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, $"{BookSelect} {BookSource} WHERE b.id=$id", ("$id", id));
        return ReadBooks(command).SingleOrDefault();
    }

    public IReadOnlyList<CatalogBook> ManageBooks(string? query)
    {
        using var connection = OpenForSearch();
        using var command = Command(connection, null, $"{BookSelect} {BookSource} WHERE {TextFilter} ORDER BY b.id DESC",
            ("$q", Db(query)));
        return ReadBooks(command);
    }

    public int SaveBook(int? id, CatalogBookDraft draft)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, id is null ? """
            INSERT INTO books(title, isbn, price, stock, active, category_id, author_id, publisher_id,
                description, cover_url, pages, year)
            VALUES($title, $isbn, $price, 0, $active, $category, $author, $publisher, $description, $cover, $pages, $year)
            RETURNING id
            """ : """
            UPDATE books SET title=$title, isbn=$isbn, price=$price, active=$active, category_id=$category,
                author_id=$author, publisher_id=$publisher, description=$description, cover_url=$cover, pages=$pages,
                year=$year WHERE id=$id RETURNING id
            """, ("$id", Db(id)), ("$title", draft.Title), ("$isbn", draft.Isbn), ("$price", draft.Price),
            ("$active", draft.Active ? 1 : 0), ("$category", Db(draft.CategoryId)), ("$author", Db(draft.AuthorId)),
            ("$publisher", Db(draft.PublisherId)), ("$description", Db(draft.Description)),
            ("$cover", Db(draft.CoverUrl)), ("$pages", Db(draft.Pages)), ("$year", Db(draft.Year)));
        try
        {
            return command.ExecuteScalar() is long saved ? (int)saved : throw new CatalogException("Không tìm thấy sách.");
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            throw new CatalogException("ISBN đã tồn tại.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new CatalogException("Thể loại, tác giả hoặc nhà xuất bản không còn tồn tại.");
        }
    }

    public void DeleteBook(int id)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, "DELETE FROM books WHERE id=$id", ("$id", id));
        Delete(command, "Sách đã có giao dịch, hãy chuyển sang ngừng kinh doanh.");
    }

    public CatalogReferences References()
    {
        using var connection = database.Open();
        return ReadReferences(connection, null);
    }

    public void SaveReference(ReferenceKind kind, int? id, CatalogReferenceDraft draft)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var table = Table(kind);
        if (kind == ReferenceKind.Category && draft.ParentId is int parent && id is int child)
        {
            using var cycle = Command(connection, transaction, """
                WITH RECURSIVE ancestors(id, parent_id) AS (
                    SELECT id, parent_id FROM categories WHERE id=$parent
                    UNION
                    SELECT c.id, c.parent_id FROM categories c JOIN ancestors a ON c.id=a.parent_id
                ) SELECT 1 FROM ancestors WHERE id=$child
                """, ("$parent", parent), ("$child", child));
            if (cycle.ExecuteScalar() is not null)
                throw new CatalogException("Thể loại cha không được tạo vòng lặp.");
        }

        var isCategory = kind == ReferenceKind.Category;
        var parentColumn = isCategory ? ", parent_id" : "";
        var parentValue = isCategory ? ", $parent" : "";
        var parentUpdate = isCategory ? ", parent_id=$parent" : "";
        using var command = Command(connection, transaction, id is null
            ? $"INSERT INTO {table}(name, description{parentColumn}) VALUES($name, $description{parentValue})"
            : $"UPDATE {table} SET name=$name, description=$description{parentUpdate} WHERE id=$id",
            ("$id", Db(id)), ("$name", draft.Name), ("$description", Db(draft.Description)), ("$parent", Db(draft.ParentId)));
        try
        {
            if (command.ExecuteNonQuery() == 0)
                throw new CatalogException("Bản ghi không còn tồn tại.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new CatalogException("Thể loại cha không hợp lệ.");
        }
        transaction.Commit();
    }

    public void DeleteReference(ReferenceKind kind, int id)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, $"DELETE FROM {Table(kind)} WHERE id=$id", ("$id", id));
        Delete(command, "Bản ghi đang được tham chiếu nên không thể xóa.");
    }

    public IReadOnlyList<Voucher> Vouchers()
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            SELECT id, code, discount_type, value, minimum_total, starts_at, ends_at, max_uses, used_count
            FROM vouchers ORDER BY id DESC
            """);
        using var reader = command.ExecuteReader();
        var vouchers = new List<Voucher>();
        while (reader.Read())
            vouchers.Add(new(reader.GetInt32(0), new(reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetInt32(4), ParseDate(reader.GetString(5)), ParseDate(reader.GetString(6)), reader.GetInt32(7)),
                reader.GetInt32(8)));
        return vouchers;
    }

    public void SaveVoucher(int? id, VoucherDraft draft)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, id is null ? """
            INSERT INTO vouchers(code, discount_type, value, minimum_total, starts_at, ends_at, max_uses)
            VALUES($code, $type, $value, $min, $start, $end, $max)
            """ : """
            UPDATE vouchers SET code=$code, discount_type=$type, value=$value, minimum_total=$min,
                starts_at=$start, ends_at=$end, max_uses=$max
            WHERE id=$id AND used_count <= $max
            """, ("$id", Db(id)), ("$code", draft.Code), ("$type", draft.DiscountType), ("$value", draft.Value),
            ("$min", draft.MinimumTotal), ("$start", FormatDate(draft.StartsAt)), ("$end", FormatDate(draft.EndsAt)),
            ("$max", draft.MaxUses));
        try
        {
            if (command.ExecuteNonQuery() == 0)
                throw new CatalogException("Voucher không còn tồn tại hoặc số lượt tối đa thấp hơn số lượt đã dùng.");
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            throw new CatalogException("Mã voucher đã tồn tại.");
        }
    }

    public void Review(string customer, int bookId, long orderId, int stars, string comment, DateTimeOffset createdAt)
    {
        using var connection = database.Open();
        using var command = Command(connection, null, """
            INSERT INTO reviews(customer, book_id, order_id, stars, comment, created_at)
            SELECT o.customer, l.book_id, o.id, $stars, $comment, $date
            FROM orders o JOIN order_lines l ON l.order_id=o.id
            WHERE o.id=$order AND o.customer=$customer AND o.status='Completed' AND l.book_id=$book
            ON CONFLICT(customer, book_id, order_id) DO NOTHING
            """, ("$customer", customer), ("$book", bookId), ("$order", orderId), ("$stars", stars),
            ("$comment", comment), ("$date", FormatDate(createdAt)));
        if (command.ExecuteNonQuery() == 0)
            throw new CatalogException("Chỉ đánh giá một lần cho sách đã mua trong đơn hoàn tất.");
    }

    private static void Delete(SqliteCommand command, string conflictMessage)
    {
        try
        {
            if (command.ExecuteNonQuery() == 0)
                throw new CatalogException("Bản ghi không còn tồn tại.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new CatalogException(conflictMessage);
        }
    }

    private SqliteConnection OpenForSearch()
    {
        var connection = database.Open();
        connection.CreateFunction<string?, string?, bool>("contains_ci",
            (field, query) => query is not null && field?.Contains(query, StringComparison.OrdinalIgnoreCase) == true, isDeterministic: true);
        return connection;
    }

    private static List<CatalogBook> ReadBooks(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var books = new List<CatalogBook>();
        while (reader.Read())
            books.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetBoolean(5), OptionalInt(reader, 6), OptionalInt(reader, 7),
                OptionalInt(reader, 8), OptionalString(reader, 9), OptionalString(reader, 10),
                OptionalString(reader, 11), OptionalString(reader, 12), OptionalString(reader, 13),
                OptionalInt(reader, 14), OptionalInt(reader, 15), reader.GetDouble(16), reader.GetInt32(17)));
        return books;
    }

    private static CatalogReferences ReadReferences(SqliteConnection connection, SqliteTransaction? transaction) =>
        new(ReadReferenceTable(connection, transaction, ReferenceKind.Category),
            ReadReferenceTable(connection, transaction, ReferenceKind.Author),
            ReadReferenceTable(connection, transaction, ReferenceKind.Publisher));

    private static List<CatalogReference> ReadReferenceTable(SqliteConnection connection, SqliteTransaction? transaction,
        ReferenceKind kind)
    {
        var parent = kind == ReferenceKind.Category ? "parent_id" : "NULL";
        using var command = Command(connection, transaction,
            $"SELECT id, name, description, {parent} FROM {Table(kind)} ORDER BY name, id");
        using var reader = command.ExecuteReader();
        var references = new List<CatalogReference>();
        while (reader.Read())
            references.Add(new(reader.GetInt32(0), reader.GetString(1), OptionalString(reader, 2), OptionalInt(reader, 3)));
        return references;
    }

    private static string Table(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Category => "categories",
        ReferenceKind.Author => "authors",
        ReferenceKind.Publisher => "publishers",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static int? OptionalInt(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private static string? OptionalString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static object Db(object? value) => value ?? DBNull.Value;
    private static string FormatDate(DateTimeOffset date) => date.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string text) => DateTimeOffset.ParseExact(text, DateFormat, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
