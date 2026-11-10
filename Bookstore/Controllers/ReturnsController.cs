using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize]
public sealed class ReturnsController(OrderService orders) : Controller
{
    [HttpGet]
    [Authorize(Roles = "Customer,Sales,Warehouse")]
    public IActionResult Index() => View(orders.Returns(User.IsInRole("Customer") ? User.Identity!.Name : null));

    [HttpPost]
    [Authorize(Roles = "Customer")]
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

    [HttpPost]
    [Authorize(Roles = "Sales,Warehouse")]
    public IActionResult Process(long id, string decision)
    {
        try
        {
            orders.ProcessReturn(id, User.Identity!.Name!, User.IsInRole("Sales") ? "Sales" : "Warehouse", decision);
            TempData["Success"] = "Đã cập nhật yêu cầu đổi trả.";
        }
        catch (OrderRuleException exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
