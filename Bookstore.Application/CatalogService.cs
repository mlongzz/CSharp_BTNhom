using Bookstore.Domain;

namespace Bookstore.Application;

public interface ICatalogRepository
{
    CatalogPage Search(CatalogFilter filter);
    BookDetails? Details(int id, string? customer);
    CatalogBook? FindBook(int id);
    IReadOnlyList<CatalogBook> ManageBooks(string? query);
    int SaveBook(int? id, CatalogBookDraft draft);
    void DeleteBook(int id);
    CatalogReferences References();
    void SaveReference(ReferenceKind kind, int? id, CatalogReferenceDraft draft);
    void DeleteReference(ReferenceKind kind, int id);
    IReadOnlyList<Voucher> Vouchers();
    void SaveVoucher(int? id, VoucherDraft draft);
    void Review(string customer, int bookId, long orderId, int stars, string comment, DateTimeOffset createdAt);
}

public sealed class CatalogService(ICatalogRepository catalog, TimeProvider clock)
{
    public CatalogPage Search(CatalogFilter filter) => catalog.Search(filter with
    {
        Query = Clean(filter.Query),
        Page = Math.Max(1, filter.Page)
    });

    public BookDetails? Details(int id, string? customer) => catalog.Details(id, customer);
    public CatalogBook? FindBook(int id) => catalog.FindBook(id);
    public IReadOnlyList<CatalogBook> ManageBooks(string? query) => catalog.ManageBooks(Clean(query));
    public CatalogReferences References() => catalog.References();
    public IReadOnlyList<Voucher> Vouchers() => catalog.Vouchers();
    public void DeleteBook(int id) => catalog.DeleteBook(id);
    public void DeleteReference(ReferenceKind kind, int id) => catalog.DeleteReference(kind, id);

    public int SaveBook(int? id, CatalogBookDraft draft)
    {
        var title = Clean(draft.Title);
        var isbn = Clean(draft.Isbn)?.ToUpperInvariant();
        var description = Clean(draft.Description);
        var cover = Clean(draft.CoverUrl);
        if (title is null || title.Length > 180 || isbn is null || isbn.Length > 40 || draft.Price <= 0)
            throw new CatalogException("Nhập tên, ISBN duy nhất và giá lớn hơn 0.");
        if (draft.CategoryId is null || draft.AuthorId is null || draft.PublisherId is null)
            throw new CatalogException("Chọn thể loại, tác giả và nhà xuất bản.");
        if (description?.Length > 4000 || draft.Pages is < 1 or > 20000 ||
            draft.Year is < 1000 || draft.Year > clock.GetUtcNow().Year + 1)
            throw new CatalogException("Kiểm tra độ dài mô tả, số trang hoặc năm xuất bản.");
        if (cover is not null && (cover.Length > 500 || !AllowedCoverUrl(cover)))
            throw new CatalogException("Ảnh bìa cần dùng đường dẫn nội bộ hoặc URL HTTPS.");

        return catalog.SaveBook(id, draft with
        {
            Title = title, Isbn = isbn, Description = description, CoverUrl = cover
        });
    }

    public void SaveReference(ReferenceKind kind, int? id, CatalogReferenceDraft draft)
    {
        var name = Clean(draft.Name);
        var description = Clean(draft.Description);
        if (name is null || name.Length > 120 || description?.Length > 1500)
            throw new CatalogException("Tên bắt buộc và tối đa 120 ký tự. Thông tin bổ sung tối đa 1.500 ký tự.");

        catalog.SaveReference(kind, id, new(name, description, kind == ReferenceKind.Category ? draft.ParentId : null));
    }

    public void SaveVoucher(int? id, VoucherDraft draft)
    {
        var code = Clean(draft.Code)?.ToUpperInvariant();
        if (code is null || code.Length > 30 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new CatalogException("Mã voucher chỉ gồm chữ, số, dấu gạch ngang hoặc gạch dưới.");
        if (draft.DiscountType is not ("Percent" or "Amount") ||
            draft.Value <= 0 || (draft.DiscountType == "Percent" && draft.Value > 100) ||
            draft.MinimumTotal < 0 || draft.MaxUses <= 0 ||
            draft.StartsAt == default || draft.EndsAt.ToUnixTimeSeconds() <= draft.StartsAt.ToUnixTimeSeconds())
            throw new CatalogException("Kiểm tra loại giảm, giá trị, thời hạn và số lượt voucher.");

        catalog.SaveVoucher(id, draft with { Code = code });
    }

    public void Review(string customer, int bookId, long orderId, int stars, string? comment)
    {
        comment = Clean(comment);
        if (stars is < 1 or > 5 || comment is null || comment.Length > 1000)
            throw new CatalogException("Chọn từ 1 đến 5 sao và viết nhận xét tối đa 1.000 ký tự.");

        catalog.Review(customer, bookId, orderId, stars, comment, clock.GetUtcNow());
    }

    private static bool AllowedCoverUrl(string value) =>
        value.StartsWith('/') && value.Length > 1 && value[1] is not ('/' or '\\') ||
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
