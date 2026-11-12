using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = AccountRoles.Warehouse)]
public sealed class DeliveryReturnsController(OrderService orders, ILogger<DeliveryReturnsController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index() => View(orders.AllOrders()
        .Where(order => order.Status is OrderStatus.DeliveryFailed or OrderStatus.ReturnedToWarehouse)
        .ToList());

    [HttpPost]
    public IActionResult Receive(long id)
    {
        try
        {
            if (!ModelState.IsValid || id <= 0)
                throw new OrderRuleException("Mã đơn hàng không hợp lệ.");

            orders.ReceiveFailedDelivery(id, User.Identity!.Name!);
            logger.LogInformation("Failed delivery for order {OrderId} received by {Warehouse}", id, User.Identity.Name);
            TempData["Success"] = $"Đã nhận hàng hoàn của đơn #{id} và cập nhật tồn kho.";
        }
        catch (OrderRuleException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
