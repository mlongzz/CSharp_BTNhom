namespace Bookstore.Domain;

public static class AccountRoles
{
    public const string Customer = "Customer";
    public const string Sales = "Sales";
    public const string Warehouse = "Warehouse";
    public const string Admin = "Admin";

    public static IReadOnlyList<string> All { get; } = [Customer, Sales, Warehouse, Admin];
}

public sealed record Account(string Username, string? Email, string Name, string Phone, string Role,
    bool IsLocked, DateTimeOffset? LastLogin);

public sealed record AccountCredentials(Account Account, string PasswordHash, DateTimeOffset? LockedUntil);

public sealed record ShippingAddress(long Id, string Recipient, string Phone, string Province,
    string District, string Ward, string Street, bool IsDefault)
{
    public string Formatted => $"{Recipient} · {Phone} · {Street}, {Ward}, {District}, {Province}";
}

public sealed record AccountMail(string Subject, string Message, DateTimeOffset CreatedAt);
