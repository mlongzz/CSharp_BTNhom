using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Bookstore.Authentication;

public static class BookstoreAccountAuthentication
{
    public static IServiceCollection AddBookstoreAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        var signingSecret = configuration["Bookstore:JwtSigningKey"];
        var key = string.IsNullOrWhiteSpace(signingSecret)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(signingSecret);
        if (key.Length < 32)
            throw new InvalidOperationException("Bookstore:JwtSigningKey must contain at least 32 UTF-8 bytes.");

        var sessions = new AccountSessions(new SymmetricSecurityKey(key));
        services.AddSingleton(sessions);
        services.AddScoped<ICustomerAccounts, SqliteCustomerAccounts>();
        services.AddScoped<IPasswordHasher<string>, PasswordHasher<string>>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = sessions.ValidationParameters;
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    context.Token = context.Request.Cookies[AccountSessions.CookieName];
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var services = context.HttpContext.RequestServices;
                    var accounts = services.GetRequiredService<ICustomerAccounts>();
                    var clock = services.GetRequiredService<TimeProvider>();
                    var username = context.Principal?.Identity?.Name;
                    var sessionId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
                    if (username is null || sessionId is null ||
                        accounts.SessionAccount(username, sessionId, clock.GetUtcNow()) is not { } account ||
                        !context.Principal!.IsInRole(account.Role))
                        context.Fail("Session is invalid or has been revoked.");
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    var url = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect(QueryHelpers.AddQueryString("/Account/Login", "returnUrl", url));
                    return Task.CompletedTask;
                },
                OnForbidden = context =>
                {
                    context.Response.Redirect("/Account/Denied");
                    return Task.CompletedTask;
                }
            };
        });
        services.AddAuthorization();
        if (SmtpSettings.From(configuration) is { } smtp)
        {
            services.AddSingleton(smtp);
            services.AddScoped<IMailDeliveryQueue, SqliteCustomerAccounts>();
            services.AddHostedService<SmtpOutboxWorker>();
        }
        return services;
    }

    public static void SeedBookstoreAccounts(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Bookstore:DemoAccounts", true))
            return;

        using var scope = app.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<ICustomerAccounts>();
        Seed("khach", "Khách hàng mẫu", "Khach123!", AccountRoles.Customer);
        Seed("nhanvien", "Nhân viên bán hàng", "Nhanvien123!", AccountRoles.Sales);
        Seed("kho", "Nhân viên kho", "Kho12345!", AccountRoles.Warehouse);
        Seed("admin", "Quản trị viên", "Admin123!", AccountRoles.Admin);

        void Seed(string username, string name, string password, string role)
        {
            if (accounts.Find(username) is null)
                accounts.Create(username, $"{username}@bookstore.test", name, BCrypt.Net.BCrypt.HashPassword(password),
                    role, sendWelcome: false);
        }
    }
}

public sealed class AccountSessions(SymmetricSecurityKey signingKey)
{
    public const string CookieName = "Bookstore.Session";
    private const string Issuer = "Bookstore";
    private const string Audience = "Bookstore.Web";
    private const string NameClaim = "name";
    private const string RoleClaim = "role";

    internal TokenValidationParameters ValidationParameters => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = signingKey,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = NameClaim,
        RoleClaimType = RoleClaim,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };

    public void SetCookie(HttpContext context, Account account, string sessionId,
        DateTimeOffset now, DateTimeOffset expires)
    {
        var token = new JwtSecurityToken(Issuer, Audience,
            [new(NameClaim, account.Username), new(RoleClaim, account.Role), new(JwtRegisteredClaimNames.Jti, sessionId)],
            now.UtcDateTime, expires.UtcDateTime, new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        context.Response.Cookies.Append(CookieName, new JwtSecurityTokenHandler().WriteToken(token), CookieOptions(context, expires));
    }

    public void ClearCookie(HttpContext context) => context.Response.Cookies.Delete(CookieName, CookieOptions(context, null));

    private static CookieOptions CookieOptions(HttpContext context, DateTimeOffset? expires) => new()
    {
        Expires = expires,
        HttpOnly = true,
        IsEssential = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/"
    };
}
