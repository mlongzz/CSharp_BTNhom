using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

public class HomeController(CatalogService catalog) : Controller
{
    public IActionResult Index(string? q, int? category, int? author, int? publisher, int? minPrice,
        int? maxPrice, int? rating, string? sort, int page = 1)
    {
        if (minPrice is < 0 || maxPrice is < 0)
            ModelState.AddModelError(nameof(minPrice), "Khoảng giá không được âm.");
        else if (minPrice > maxPrice)
            ModelState.AddModelError(nameof(maxPrice), "Giá đến phải lớn hơn hoặc bằng Giá từ.");

        if (rating is < 1 or > 5)
            ModelState.AddModelError(nameof(rating), "Điểm đánh giá phải từ 1 đến 5 sao.");

        var filter = new CatalogFilter(q, category, author, publisher, minPrice, maxPrice, rating, sort, page);
        if (!ModelState.IsValid)
            return View(new CatalogPage([], filter, catalog.References(), 0, 1));

        return View(catalog.Search(filter));
    }

    public IActionResult Details(int id)
    {
        var customer = User.IsInRole("Customer") ? User.Identity!.Name : null;
        var details = catalog.Details(id, customer);
        return details is null ? NotFound() : View(details);
    }

    [HttpPost]
    [Authorize(Roles = "Customer")]
    public IActionResult Review(int id, long orderId, int stars, string? comment)
    {
        try
        {
            if (!ModelState.IsValid)
                throw new CatalogException("Kiểm tra thông tin đánh giá.");
            catalog.Review(User.Identity!.Name!, id, orderId, stars, comment);
            TempData["Success"] = "Đã lưu đánh giá.";
        }
        catch (CatalogException exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [IgnoreAntiforgeryToken]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
}
