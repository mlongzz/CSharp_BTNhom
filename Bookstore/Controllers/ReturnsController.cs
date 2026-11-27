using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = AccountRoles.Customer)]
public sealed class ReturnsController(OrderService orders) : Controller
{
    [HttpGet]
    public IActionResult Index() => View(orders.Returns(User.Identity!.Name!));

    [HttpPost]
    public IActionResult RequestReturn(long orderId, int bookId, string? reason, string outcome)
    {
        try
        {
            orders.RequestReturn(orderId, bookId, User.Identity!.Name!, reason, outcome);
            TempData["Success"] = "Đã gửi yêu cầu đổi trả.";
        }
        catch (OrderRuleException exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
