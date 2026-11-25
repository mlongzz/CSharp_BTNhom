using System.Globalization;
using Bookstore.Domain;

namespace Bookstore.Desktop.Services;

public sealed partial class BookstoreApiClient
{
    public Task<List<Order>> GetOrdersAsync(bool shipping, string? search, OrderStatus? status,
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var query = new List<string> { shipping ? "shipping=true" : "shipping=false" };
        if (!string.IsNullOrWhiteSpace(search))
            query.Add("q=" + Uri.EscapeDataString(search.Trim()));
        if (status is { } selectedStatus)
            query.Add("status=" + selectedStatus);
        if (from is { } start)
            query.Add("from=" + start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (to is { } end)
            query.Add("to=" + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return GetAsync<List<Order>>("api/admin/orders?" + string.Join('&', query), cancellationToken);
    }

    public Task UpdateOrderAsync(long id, OrderAction action, string? carrier, string? tracking, string? reason,
        CancellationToken cancellationToken) => SendAsync(HttpMethod.Post,
        FormattableString.Invariant($"api/admin/orders/{id}/action"), new { action, carrier, tracking, reason }, cancellationToken);

    public Task<List<ReturnRequest>> GetReturnsAsync(string? search, string? status, CancellationToken cancellationToken)
    {
        var url = "api/admin/returns?q=" + Uri.EscapeDataString(search?.Trim() ?? string.Empty) +
            "&status=" + Uri.EscapeDataString(status ?? string.Empty);
        return GetAsync<List<ReturnRequest>>(url, cancellationToken);
    }

    public Task UpdateReturnAsync(long id, string expectedStatus, string decision, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, FormattableString.Invariant($"api/admin/returns/{id}/action"),
            new { expectedStatus, decision }, cancellationToken);
}
