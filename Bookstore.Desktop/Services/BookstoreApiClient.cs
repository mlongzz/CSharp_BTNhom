
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Bookstore.Desktop.Services;

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed record BookRow(
    int Id,
    string Title,
    string Isbn,
    int Price,
    int Stock,
    bool Active);

public sealed class BookstoreApiClient : IDisposable
{
    private readonly HttpClient _http;

    public BookstoreApiClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<bool> LoginAsync(
        string username, string password)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/desktop/auth/login",
            new { username, password });

        if (response.StatusCode is
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<LoginResponse>();

        if (string.IsNullOrWhiteSpace(result?.AccessToken))
            throw new InvalidOperationException(
                "API không trả về access token.");

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", result.AccessToken);

        return true;
    }

    public async Task<List<BookRow>> GetBooksAsync(
        string? search = null)
    {
        string url = "api/admin/books";

        if (!string.IsNullOrWhiteSpace(search))
            url += "?q=" + Uri.EscapeDataString(search.Trim());

        return await _http.GetFromJsonAsync<List<BookRow>>(url)
               ?? new List<BookRow>();
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (_http.DefaultRequestHeaders.Authorization != null)
            {
                using var response = await _http.PostAsync(
                    "api/desktop/auth/logout", null);
                response.EnsureSuccessStatusCode();
            }
        }
        finally
        {
            _http.DefaultRequestHeaders.Authorization = null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
