using Bookstore.Application;
using Bookstore.Domain;

namespace Bookstore.Desktop.Services;

public sealed partial class BookstoreApiClient
{
    public Task<List<Account>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<Account>>("api/admin/accounts", cancellationToken);

    public Task UpdateAccountAccessAsync(string username, string role, bool locked, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"api/admin/accounts/{Uri.EscapeDataString(username)}/access", new { role, locked }, cancellationToken);

    public Task<BookstoreReport> GetReportAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default) =>
        GetAsync<BookstoreReport>("api/admin/reports" + ReportQuery(from, through), cancellationToken);

    public async Task<byte[]> ExportReportAsync(DateOnly from, DateOnly through, string section,
        CancellationToken cancellationToken = default)
    {
        var url = "api/admin/reports/export" + ReportQuery(from, through) + "&section=" + Uri.EscapeDataString(section);
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static string ReportQuery(DateOnly from, DateOnly through) =>
        FormattableString.Invariant($"?from={from:yyyy-MM-dd}&through={through:yyyy-MM-dd}");
}
