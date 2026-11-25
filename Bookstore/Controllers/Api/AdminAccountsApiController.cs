using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/admin/accounts")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AccountRoles.Admin)]
[IgnoreAntiforgeryToken]
public sealed class AdminAccountsApiController(ICustomerAccounts accounts) : ControllerBase
{
    public sealed record AccessRequest(string? Role, bool? Locked);

    [HttpGet]
    public IReadOnlyList<Account> List() => accounts.List();

    [HttpPut("{username}/access")]
    public IActionResult UpdateAccess(string username, AccessRequest input)
    {
        if (input.Role is not { } role || !AccountRoles.All.Contains(role) || input.Locked is not { } locked)
            return Problem(detail: "Vai trò hoặc trạng thái tài khoản không hợp lệ.", statusCode: 400);

        if (string.Equals(username, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            return Problem(detail: "Không thể đổi quyền hoặc khóa tài khoản đang đăng nhập.", statusCode: 400);

        if (accounts.UpdateAccess(username, role, locked))
            return NoContent();

        var account = accounts.Find(username);
        if (account is null)
            return Problem(detail: "Không tìm thấy tài khoản.", statusCode: 404);

        if (account.Role == role && account.IsLocked == locked)
            return NoContent();

        return Problem(detail: "Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.", statusCode: 409);
    }
}
