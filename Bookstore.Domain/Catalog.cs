namespace Bookstore.Domain;

public enum ReferenceKind { Category, Author, Publisher }

public sealed record CatalogReference(int Id, string Name, string? Description, int? ParentId);

public sealed record CatalogReferences(IReadOnlyList<CatalogReference> Categories,
    IReadOnlyList<CatalogReference> Authors, IReadOnlyList<CatalogReference> Publishers);

public sealed record CatalogBook(int Id, string Title, string Isbn, int Price, int Stock, bool Active,
    int? CategoryId, int? AuthorId, int? PublisherId, string? Category, string? Author, string? Publisher,
    string? Description, string? CoverUrl, int? Pages, int? Year, double Rating, int Reviews);

public sealed record CatalogFilter(string? Query, int? Category, int? Author, int? Publisher,
    int? MinimumPrice, int? MaximumPrice, int? Rating, string? Sort, int Page);

public sealed record CatalogPage(IReadOnlyList<CatalogBook> Books, CatalogFilter Filter,
    CatalogReferences References, int Total, int PageSize);

public sealed record BookDetails(CatalogBook Book, IReadOnlyList<CatalogReview> Reviews,
    IReadOnlyList<CatalogBook> Related, IReadOnlyList<long> EligibleOrders);

public sealed record CatalogReview(string Customer, int Stars, string Comment, DateTimeOffset CreatedAt);

public sealed record CatalogBookDraft(string Title, string Isbn, int Price, bool Active,
    int? CategoryId, int? AuthorId, int? PublisherId, string? Description, string? CoverUrl, int? Pages, int? Year);

public sealed record CatalogReferenceDraft(string Name, string? Description, int? ParentId);

public sealed record VoucherDraft(string Code, string DiscountType, int Value, int MinimumTotal,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, int MaxUses);

public sealed record Voucher(int Id, VoucherDraft Draft, int UsedCount);

public sealed class CatalogException(string message) : Exception(message);
