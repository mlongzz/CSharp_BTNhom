using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Bookstore.Application;
using Bookstore.Authentication;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

public sealed class AccountController(ICustomerAccounts accounts, IPasswordHasher<string> legacyPasswords,
    AccountSessions sessions, TimeProvider clock) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost]
    public IActionResult Register(string? email, string? password)
    {
        email = email?.Trim().ToLowerInvariant();
        ViewData["Email"] = email;
        if (!AccountInput.ValidEmail(email))
            ModelState.AddModelError("", "Email không hợp lệ hoặc đã được sử dụng.");
        if (!AccountInput.ValidNewPassword(password))
            ModelState.AddModelError("", "Mật khẩu phải có ít nhất 8 ký tự gồm chữ và số, tối đa 72 byte UTF-8.");
        if (!ModelState.IsValid)
            return View();

        if (!accounts.Create(Guid.NewGuid().ToString("N"), email!, "", BCrypt.Net.BCrypt.HashPassword(password!),
                AccountRoles.Customer, sendWelcome: true))
        {
            ModelState.AddModelError("", "Email không hợp lệ hoặc đã được sử dụng.");
            return View();
        }

        TempData["Success"] = "Đăng ký thành công. Bạn có thể đăng nhập ngay.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    public IActionResult Login(string? username, string? password, string? returnUrl, string? email)
    {
        ViewData["ReturnUrl"] = returnUrl;
        username = (email ?? username)?.Trim().ToLowerInvariant();
        ViewData["Identity"] = username;
        var credentials = username is { Length: > 0 and <= 254 } ? accounts.Credentials(username) : null;
        var now = clock.GetUtcNow();

        if (credentials?.Account.IsLocked == true)
            return Failed("Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");
        if (credentials?.LockedUntil > now)
            return Failed("Đăng nhập đang tạm khóa. Vui lòng thử lại sau 5 phút.");
        if (credentials is null || password is not { Length: > 0 and <= 128 } || !Verify(credentials, password))
        {
            if (credentials is not null)
                accounts.RecordFailedAttempt(credentials.Account.Username, now);
            return Failed("Email hoặc mật khẩu không đúng.");
        }

        var account = credentials.Account;
        var expires = now.AddHours(8);
        var sessionId = Guid.NewGuid().ToString("N");
        if (!accounts.StartSession(account.Username, credentials.PasswordHash, sessionId, now, expires))
            return Failed("Không thể đăng nhập. Vui lòng thử lại.");

        if (User.Identity?.Name is string previousAccount && User.FindFirstValue(JwtRegisteredClaimNames.Jti) is string previousSession)
            accounts.EndSession(previousAccount, previousSession);
        HttpContext.Session.Clear();
        sessions.SetCookie(HttpContext, account, sessionId, now, expires);

        if (Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return account.Role switch
        {
            AccountRoles.Sales => RedirectToAction("Index", "AdminOrders"),
            AccountRoles.Warehouse => RedirectToAction("Index", "Warehouse"),
            AccountRoles.Admin => RedirectToAction("Index", "AdminAccounts"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    [Authorize]
    [HttpPost]
    public IActionResult Logout()
    {
        if (User.Identity?.Name is string username && User.FindFirstValue(JwtRegisteredClaimNames.Jti) is string id)
            accounts.EndSession(username, id);

        sessions.ClearCookie(HttpContext);
        HttpContext.Session.Clear();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Denied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private IActionResult Failed(string message)
    {
        ModelState.AddModelError("", message);
        return View(nameof(Login));
    }

    private bool Verify(AccountCredentials credentials, string password) =>
        credentials.PasswordHash.StartsWith("$2", StringComparison.Ordinal)
            ? Encoding.UTF8.GetByteCount(password) <= 72 && BCrypt.Net.BCrypt.Verify(password, credentials.PasswordHash)
            : legacyPasswords.VerifyHashedPassword(credentials.Account.Username, credentials.PasswordHash, password) !=
                PasswordVerificationResult.Failed;
}
