using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = AccountRoles.Admin)]
public sealed class AdminAccountsController(ICustomerAccounts accounts) : Controller
{
    [HttpGet]
    public IActionResult Index() => View(accounts.List());

    [HttpPost]
    public IActionResult Update(string? username, string? role, bool locked)
    {
        if (username is null || role is null || !AccountRoles.All.Contains(role) || !ModelState.IsValid)
            return BadRequest();

        if (username == User.Identity?.Name)
        {
            TempData["Error"] = "Không thể đổi quyền hoặc khóa tài khoản đang quản trị.";
            return RedirectToAction(nameof(Index));
        }

        if (accounts.UpdateAccess(username, role, locked) ||
            accounts.Find(username) is { } account && account.Role == role && account.IsLocked == locked)
            TempData["Success"] = "Đã lưu trạng thái và vai trò.";
        else
            TempData["Error"] = "Không thể cập nhật tài khoản.";
        return RedirectToAction(nameof(Index));
    }
}
