using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

public class HomeController(CatalogService catalog) : Controller
{
    public IActionResult Index(string? q, int? category, int? author, int? publisher, int? minPrice,
        int? maxPrice, int? rating, string? sort, int page = 1) =>
        View(catalog.Search(new CatalogFilter(q, category, author, publisher, minPrice, maxPrice, rating, sort, page)));

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
