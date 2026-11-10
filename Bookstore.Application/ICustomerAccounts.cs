using Bookstore.Domain;

namespace Bookstore.Application;

public interface ICustomerAccounts
{
    bool Create(string username, string email, string name, string passwordHash, string role, bool sendWelcome);
    AccountCredentials? Credentials(string identity);
    string? PasswordHash(string username);
    void RecordFailedAttempt(string username, DateTimeOffset now);
    bool StartSession(string username, string passwordHash, string sessionId, DateTimeOffset now,
        DateTimeOffset expiresAt);
    Account? SessionAccount(string username, string sessionId, DateTimeOffset now);
    void EndSession(string username, string sessionId);

    Account? Find(string username);
    IReadOnlyList<Account> List();
    bool UpdateProfile(string username, string email, string name, string phone);
    bool UpdateAccess(string username, string role, bool locked);
    IReadOnlyList<ShippingAddress> Addresses(string username);
    ShippingAddress? Address(string username, long id);
    bool SaveAddress(string username, ShippingAddress address);
    bool DeleteAddress(string username, long id);
    void QueueEmail(string username, string subject, string message);
    IReadOnlyList<AccountMail> Mail(string username);
}
