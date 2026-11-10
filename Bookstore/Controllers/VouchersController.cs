using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = "Admin")]
public sealed class VouchersController(CatalogService catalog, TimeProvider clock) : Controller
{
    [HttpGet]
    public IActionResult Index(int? editId)
    {
        var vouchers = catalog.Vouchers();
        var voucher = editId is int id ? vouchers.SingleOrDefault(item => item.Id == id) : null;
        if (editId is not null && voucher is null)
            return NotFound();

        var today = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).Date;
        return View(new VouchersPage(vouchers, voucher is null
            ? new() { StartsAt = today, EndsAt = today.AddDays(30), MaxUses = 1 }
            : VoucherInput.From(voucher)));
    }

    [HttpPost]
    public IActionResult Save([Bind(Prefix = "Input")] VoucherInput input)
    {
        try
        {
            if (!ModelState.IsValid)
                throw new CatalogException("Kiểm tra các trường dữ liệu voucher.");
            catalog.SaveVoucher(input.Id, input.Draft());
            TempData["Success"] = "Đã lưu voucher.";
            return RedirectToAction(nameof(Index));
        }
        catch (CatalogException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(nameof(Index), new VouchersPage(catalog.Vouchers(), input));
        }
    }
}
