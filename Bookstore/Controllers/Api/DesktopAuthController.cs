
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Bookstore.Application;
using Bookstore.Authentication;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/desktop/auth")]
[IgnoreAntiforgeryToken]
public sealed class DesktopAuthController(
    ICustomerAccounts accounts,
    IPasswordHasher<string> legacyPasswords,
    AccountSessions sessions,
    TimeProvider clock) : ControllerBase
{
    public sealed record LoginRequest(
        string Username,
        string Password);

    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] LoginRequest? input)
    {
        string? username =
            input?.Username?.Trim().ToLowerInvariant();

        string? password = input?.Password;

        if (string.IsNullOrWhiteSpace(username) ||
            username.Length > 254 ||
            password is not { Length: > 0 and <= 128 })
        {
            return Unauthorized();
        }

        var credentials = accounts.Credentials(username);
        var now = clock.GetUtcNow();

        if (credentials is null ||
            credentials.Account.IsLocked ||
            credentials.LockedUntil > now)
        {
            return Unauthorized();
        }

        bool validPassword;

        if (credentials.PasswordHash.StartsWith(
            "$2", StringComparison.Ordinal))
        {
            validPassword =
                Encoding.UTF8.GetByteCount(password) <= 72 &&
                BCrypt.Net.BCrypt.Verify(
                    password, credentials.PasswordHash);
        }
        else
        {
            validPassword =
                legacyPasswords.VerifyHashedPassword(
                    credentials.Account.Username,
                    credentials.PasswordHash,
                    password)
                != PasswordVerificationResult.Failed;
        }

        if (!validPassword)
        {
            accounts.RecordFailedAttempt(
                credentials.Account.Username, now);

            return Unauthorized();
        }

        // Chỉ Admin được sử dụng Desktop.
        if (credentials.Account.Role != AccountRoles.Admin)
        {
            return Forbid();
        }

        var expires = now.AddHours(8);
        var sessionId = Guid.NewGuid().ToString("N");

        bool sessionCreated = accounts.StartSession(
            credentials.Account.Username,
            credentials.PasswordHash,
            sessionId,
            now,
            expires);

        if (!sessionCreated)
        {
            return Unauthorized();
        }

        string token = sessions.CreateToken(
            credentials.Account,
            sessionId,
            now,
            expires);

        return Ok(new
        {
            accessToken = token,
            expiresAt = expires,
            username = credentials.Account.Username,
            role = credentials.Account.Role
        });
    }

    [HttpPost("logout")]
    [Authorize(Roles = AccountRoles.Admin)]
    public IActionResult Logout()
    {
        string? username = User.Identity?.Name;

        string? sessionId =
            User.FindFirstValue(JwtRegisteredClaimNames.Jti);

        if (username is not null && sessionId is not null)
        {
            accounts.EndSession(username, sessionId);
        }

        return NoContent();
    }
}
