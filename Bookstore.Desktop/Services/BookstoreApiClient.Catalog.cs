using Bookstore.Domain;

namespace Bookstore.Desktop.Services;

public sealed partial class BookstoreApiClient
{
    private const string BooksUrl = "api/admin/books";
    private const string ReferencesUrl = "api/admin/catalog/references";

    public Task<List<CatalogBook>> GetCatalogBooksAsync(string? search = null, CancellationToken cancellationToken = default) =>
        GetAsync<List<CatalogBook>>(string.IsNullOrWhiteSpace(search)
            ? BooksUrl
            : $"{BooksUrl}?q={Uri.EscapeDataString(search.Trim())}", cancellationToken);

    public Task<CatalogBook> GetCatalogBookAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync<CatalogBook>($"{BooksUrl}/{id}", cancellationToken);

    public Task SaveBookAsync(int? id, CatalogBookDraft draft, CancellationToken cancellationToken = default) =>
        id is int bookId
            ? SendAsync(HttpMethod.Put, $"{BooksUrl}/{bookId}", draft, cancellationToken)
            : SendAsync(HttpMethod.Post, BooksUrl, draft, cancellationToken);

    public Task SetBookActiveAsync(int id, bool active, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{BooksUrl}/{id}/availability", new { active }, cancellationToken);

    public Task DeleteBookAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{BooksUrl}/{id}", cancellationToken: cancellationToken);

    public Task<CatalogReferences> GetCatalogReferencesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<CatalogReferences>(ReferencesUrl, cancellationToken);

    public Task SaveCatalogReferenceAsync(ReferenceKind kind, int? id, CatalogReferenceDraft draft,
        CancellationToken cancellationToken = default) =>
        id is int referenceId
            ? SendAsync(HttpMethod.Put, $"{ReferencesUrl}/{kind}/{referenceId}", draft, cancellationToken)
            : SendAsync(HttpMethod.Post, $"{ReferencesUrl}/{kind}", draft, cancellationToken);

    public Task DeleteCatalogReferenceAsync(ReferenceKind kind, int id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{ReferencesUrl}/{kind}/{id}", cancellationToken: cancellationToken);
}
