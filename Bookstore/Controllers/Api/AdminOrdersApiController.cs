using System.Globalization;
using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/admin/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AccountRoles.Admin)]
[IgnoreAntiforgeryToken]
public sealed class AdminOrdersApiController(OrderService orders) : ControllerBase
{
    public sealed record OrderUpdate(OrderAction Action, string? Carrier, string? Tracking, string? Reason);

    [HttpGet]
    public IActionResult Index(string? q, OrderStatus? status, DateOnly? from, DateOnly? to, bool shipping = false)
    {
        if (from > to)
            return Problem(detail: "Đến ngày phải bằng hoặc sau Từ ngày.", statusCode: 400);

        var search = q?.Trim();
        var results = orders.AllOrders().Where(order =>
            (!shipping || order.Status is not (OrderStatus.Pending or OrderStatus.Cancelled)) &&
            (status is null || order.Status == status) &&
            (string.IsNullOrEmpty(search) || Matches(order, search)) &&
            (from is null || BusinessDate(order.CreatedAt) >= from) &&
            (to is null || BusinessDate(order.CreatedAt) <= to));

        return Ok(results.ToList());
    }

    [HttpPost("{id:long}/action")]
    public IActionResult Update(long id, OrderUpdate input)
    {
        if (id <= 0 || input.Action is not (OrderAction.Confirmed or OrderAction.Rejected or OrderAction.Packing or
            OrderAction.Shipped or OrderAction.Completed or OrderAction.DeliveryFailed or OrderAction.ReturnedToWarehouse))
            return Problem(detail: "Mã đơn hoặc thao tác không hợp lệ.", statusCode: 400);

        var actor = User.Identity!.Name!;
        try
        {
            switch (input.Action)
            {
                case OrderAction.Confirmed:
                    orders.Confirm(id, actor);
                    break;
                case OrderAction.ReturnedToWarehouse:
                    orders.ReceiveFailedDelivery(id, actor);
                    break;
                default:
                    orders.Fulfill(id, actor, input.Action, input.Carrier, input.Tracking, input.Reason);
                    break;
            }
            return NoContent();
        }
        catch (OrderRuleException exception)
        {
            return Problem(detail: exception.Message, statusCode: 409);
        }
    }

    private static bool Matches(Order order, string search) =>
        order.Id.ToString(CultureInfo.InvariantCulture) == search ||
        order.Customer.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        order.Carrier?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
        order.TrackingCode?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static DateOnly? BusinessDate(DateTimeOffset? date) => date is null
        ? null : DateOnly.FromDateTime(date.Value.ToOffset(TimeSpan.FromHours(7)).DateTime);
}
