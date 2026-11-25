using System.Globalization;
using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/admin/returns")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AccountRoles.Admin)]
[IgnoreAntiforgeryToken]
public sealed class AdminReturnsApiController(OrderService orders) : ControllerBase
{
    public sealed record ReturnUpdate(string ExpectedStatus, string Decision);

    [HttpGet]
    public IActionResult Index(string? q, string? status)
    {
        var search = q?.Trim();
        return Ok(orders.Returns().Where(request =>
            (string.IsNullOrWhiteSpace(status) || request.Status == status) &&
            (string.IsNullOrEmpty(search) || Matches(request, search))).ToList());
    }

    [HttpPost("{id:long}/action")]
    public IActionResult Process(long id, ReturnUpdate input)
    {
        if (id <= 0 || input.ExpectedStatus is not ("Pending" or "Approved") ||
            input.Decision is not ("Approve" or "Reject" or "Receive"))
            return Problem(detail: "Mã yêu cầu hoặc thao tác đổi trả không hợp lệ.", statusCode: 400);

        try
        {
            orders.ProcessReturnAsAdmin(id, User.Identity!.Name!, input.ExpectedStatus, input.Decision);
            return NoContent();
        }
        catch (OrderRuleException exception)
        {
            return Problem(detail: exception.Message, statusCode: 409);
        }
    }

    private static bool Matches(ReturnRequest request, string search) =>
        request.Id.ToString(CultureInfo.InvariantCulture) == search ||
        request.OrderId.ToString(CultureInfo.InvariantCulture) == search ||
        request.Customer.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        request.Title.Contains(search, StringComparison.OrdinalIgnoreCase);
}
