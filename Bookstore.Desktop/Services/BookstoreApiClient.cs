
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Bookstore.Desktop.Services;

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string Username);

public sealed record BookRow(
    int Id,
    string Title,
    string Isbn,
    int Price,
    int Stock,
    bool Active);

public sealed partial class BookstoreApiClient : IDisposable
{
    private readonly HttpClient _http;
    private string? accessToken;

    public string? Username { get; private set; }

    public BookstoreApiClient(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") ||
            address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0)
            throw new ArgumentException("Địa chỉ backend phải là URL HTTP hoặc HTTPS hợp lệ.", nameof(baseUrl));

        _http = new HttpClient
        {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<bool> LoginAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/desktop/auth/login",
            new { username, password }, cancellationToken);

        if (response.StatusCode is
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content
            .ReadFromJsonAsync<LoginResponse>(cancellationToken);

        if (string.IsNullOrWhiteSpace(result?.AccessToken))
            throw new InvalidOperationException(
                "API không trả về access token.");

        accessToken = result.AccessToken;
        Username = result.Username;

        return true;
    }

    public async Task<List<BookRow>> GetBooksAsync(
        string? search = null)
    {
        string url = "api/admin/books";

        if (!string.IsNullOrWhiteSpace(search))
            url += "?q=" + Uri.EscapeDataString(search.Trim());

        return await GetAsync<List<BookRow>>(url);
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (accessToken is not null)
                await SendAsync(HttpMethod.Post, "api/desktop/auth/logout");
        }
        finally
        {
            accessToken = null;
            Username = null;
        }
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new HttpRequestException("Backend trả về dữ liệu trống.");
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Backend trả về dữ liệu không đúng định dạng.", ex);
        }
    }

    private async Task SendAsync(HttpMethod method, string url, object? body = null,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (accessToken is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
            HttpStatusCode.Forbidden => "Tài khoản không có quyền thực hiện thao tác này.",
            _ => await ReadErrorAsync(response.Content, cancellationToken)
                ?? $"Backend trả về lỗi HTTP {(int)response.StatusCode}."
        };
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static async Task<string?> ReadErrorAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.OrdinalIgnoreCase) != true)
            return null;

        try
        {
            using var error = await content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            if (error?.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var name in new[] { "detail", "error", "message", "title" })
            {
                if (error.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
