using System.Net.Mail;
using System.Text;

namespace Bookstore.Models;

internal static class AccountInput
{
    public static bool ValidEmail(string? email) =>
        email is { Length: > 0 and <= 254 } && MailAddress.TryCreate(email, out var parsed) &&
        string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase) && parsed.Host.Contains('.');

    public static bool ValidPhone(string? phone) =>
        phone is { Length: 10 } && phone[0] == '0' && phone.All(char.IsAsciiDigit);

    public static bool ValidNewPassword(string? password) =>
        password is { Length: >= 8 and <= 72 } && Encoding.UTF8.GetByteCount(password) <= 72 &&
        password.Any(char.IsLetter) && password.Any(char.IsDigit);
}

