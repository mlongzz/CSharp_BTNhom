using Bookstore.Domain;

namespace Bookstore.Models;

public sealed class CatalogBookInput
{
    public int? Id { get; set; }
    public string? Title { get; set; }
    public string? Isbn { get; set; }
    public int Price { get; set; }
    public bool Active { get; set; } = true;
    public int? CategoryId { get; set; }
    public int? AuthorId { get; set; }
    public int? PublisherId { get; set; }
    public string? Description { get; set; }
    public string? CoverUrl { get; set; }
    public int? Pages { get; set; }
    public int? Year { get; set; }

    public CatalogBookDraft Draft() => new(Title ?? "", Isbn ?? "", Price, Active, CategoryId, AuthorId,
        PublisherId, Description, CoverUrl, Pages, Year);

    public static CatalogBookInput From(CatalogBook book) => new()
    {
        Id = book.Id, Title = book.Title, Isbn = book.Isbn, Price = book.Price, Active = book.Active,
        CategoryId = book.CategoryId, AuthorId = book.AuthorId, PublisherId = book.PublisherId,
        Description = book.Description, CoverUrl = book.CoverUrl, Pages = book.Pages, Year = book.Year
    };
}

public sealed record CatalogAdminPage(IReadOnlyList<CatalogBook> Books, CatalogReferences References,
    CatalogBookInput Input, string? Query);

public sealed class CatalogReferenceInput
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? ParentId { get; set; }
}

public sealed record CatalogReferencePage(ReferenceKind Kind, IReadOnlyList<CatalogReference> References,
    CatalogReferenceInput Input);

public sealed class VoucherInput
{
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(7);

    public int? Id { get; set; }
    public string? Code { get; set; }
    public string? DiscountType { get; set; } = "Percent";
    public int Value { get; set; }
    public int MinimumTotal { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int MaxUses { get; set; }

    public VoucherDraft Draft() => new(Code ?? "", DiscountType ?? "", Value, MinimumTotal,
        ToOffset(StartsAt), ToOffset(EndsAt), MaxUses);

    public static VoucherInput From(Voucher voucher) => new()
    {
        Id = voucher.Id, Code = voucher.Draft.Code, DiscountType = voucher.Draft.DiscountType,
        Value = voucher.Draft.Value, MinimumTotal = voucher.Draft.MinimumTotal, MaxUses = voucher.Draft.MaxUses,
        StartsAt = voucher.Draft.StartsAt.ToOffset(LocalOffset).DateTime,
        EndsAt = voucher.Draft.EndsAt.ToOffset(LocalOffset).DateTime
    };

    private static DateTimeOffset ToOffset(DateTime? time) => time is DateTime { Year: >= 2 } value
        ? new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), LocalOffset)
        : default;
}

public sealed record VouchersPage(IReadOnlyList<Voucher> Vouchers, VoucherInput Input);
