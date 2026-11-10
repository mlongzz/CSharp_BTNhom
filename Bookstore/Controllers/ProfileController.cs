using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = AccountRoles.Customer)]
public sealed class ProfileController(ICustomerAccounts accounts) : Controller
{
    private string Username => User.Identity!.Name!;

    [HttpGet]
    public IActionResult Index() => accounts.Find(Username) is { } account ? Profile(account) : NotFound();

    [HttpPost]
    public IActionResult Update(string? name, string? email, string? phone)
    {
        if (accounts.Find(Username) is not { } account)
            return NotFound();

        name = name?.Trim() ?? "";
        email = email?.Trim().ToLowerInvariant();
        phone = phone?.Trim();
        if (name.Length is < 1 or > 120)
            ModelState.AddModelError("", "Họ tên cần từ 1 đến 120 ký tự.");
        if (!AccountInput.ValidEmail(email))
            ModelState.AddModelError("", "Email không hợp lệ hoặc đã được sử dụng.");
        if (!AccountInput.ValidPhone(phone))
            ModelState.AddModelError("", "Số điện thoại cần 10 chữ số và bắt đầu bằng 0.");
        if (ModelState.IsValid && !accounts.UpdateProfile(Username, email!, name, phone!))
            ModelState.AddModelError("", "Email không hợp lệ hoặc đã được sử dụng.");
        if (!ModelState.IsValid)
            return Profile(account with { Name = name, Email = email, Phone = phone ?? "" });

        TempData["Success"] = "Đã cập nhật thông tin cá nhân.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Address(long id = 0)
    {
        if (id == 0)
            return View(new AddressInput());
        return accounts.Address(Username, id) is { } address ? View(AddressInput.From(address)) : NotFound();
    }

    [HttpPost]
    public IActionResult Address(AddressInput input)
    {
        if (!AccountInput.ValidPhone(input.Phone))
            ModelState.AddModelError(nameof(input.Phone), "Số điện thoại cần 10 chữ số và bắt đầu bằng 0.");

        if (!ModelState.IsValid)
            return View(input);
        if (!accounts.SaveAddress(Username, input.ToAddress()))
            return NotFound();

        TempData["Success"] = "Đã lưu địa chỉ giao hàng.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Delete(long id) =>
        accounts.Address(Username, id) is { } address ? View(address) : NotFound();

    [HttpPost, ActionName(nameof(Delete))]
    public IActionResult ConfirmDelete(long id)
    {
        if (!accounts.DeleteAddress(Username, id))
            return NotFound();

        TempData["Success"] = "Đã xóa địa chỉ giao hàng.";
        return RedirectToAction(nameof(Index));
    }

    private ViewResult Profile(Account account) =>
        View(nameof(Index), new ProfileViewModel(account, accounts.Addresses(Username), accounts.Mail(Username)));
}

