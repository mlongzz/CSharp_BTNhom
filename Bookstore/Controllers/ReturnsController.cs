using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

public sealed class ReturnsController(OrderService orders) : Controller
{
    [HttpGet]
    [Authorize(Roles = AccountRoles.Customer + "," + AccountRoles.Admin)]
    public IActionResult Index() => View(orders.Returns(User.IsInRole(AccountRoles.Admin) ? null : User.Identity!.Name));

    [HttpPost]
    [Authorize(Roles = AccountRoles.Customer)]
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
    [Authorize(Roles = AccountRoles.Admin)]
    public IActionResult Process(long id, string? expectedStatus, string? decision)
    {
        try
        {
            if (!ModelState.IsValid || id <= 0 || expectedStatus is not ("Pending" or "Approved") ||
                decision is not ("Approve" or "Reject" or "Receive"))
                throw new OrderRuleException("Thông tin xử lý yêu cầu đổi trả không hợp lệ. Vui lòng tải lại trang.");

            orders.ProcessReturnAsAdmin(id, User.Identity!.Name!, expectedStatus, decision);
            TempData["Success"] = "Đã cập nhật yêu cầu đổi trả.";
        }
        catch (OrderRuleException exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
