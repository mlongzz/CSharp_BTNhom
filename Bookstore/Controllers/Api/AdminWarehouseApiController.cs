using System.Text.Json.Serialization;
using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/admin/warehouse")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AccountRoles.Admin)]
[IgnoreAntiforgeryToken]
public sealed class AdminWarehouseApiController(ReceivingService receiving) : ControllerBase
{
    private string Actor => User.Identity!.Name!;

    [HttpGet("suppliers")]
    public IReadOnlyList<Supplier> Suppliers() => receiving.Suppliers();

    [HttpPost("suppliers")]
    public IActionResult CreateSupplier(SupplierRequest request) => SaveSupplier(0, request);

    [HttpPut("suppliers/{id:int:min(1)}")]
    public IActionResult UpdateSupplier(int id, SupplierRequest request) => SaveSupplier(id, request);

    [HttpDelete("suppliers/{id:int:min(1)}")]
    public IActionResult DeleteSupplier(int id) => Run(() => receiving.DeleteSupplier(id));

    [HttpGet("purchases")]
    public IReadOnlyList<PurchaseOrder> Purchases() => receiving.Purchases();

    [HttpPost("purchases")]
    public IActionResult CreatePurchase(CreatePurchaseRequest request)
    {
        if (request.Lines is null || request.Lines.Values.Any(line => line is null))
            return RuleError("Chọn sách và nhập số lượng cùng giá nhập cho từng dòng.");

        try
        {
            var id = receiving.Create(Actor, request.SupplierId, request.Lines);
            return StatusCode(StatusCodes.Status201Created, new { id });
        }
        catch (ReceivingRuleException exception)
        {
            return RuleError(exception.Message);
        }
    }

    [HttpPost("purchases/{id:long:min(1)}/approve")]
    public IActionResult Approve(long id) => Run(() => receiving.Approve(id, Actor));

    [HttpPost("purchases/{id:long:min(1)}/receive")]
    public IActionResult Receive(long id, ReceivePurchaseRequest request) =>
        Run(() => receiving.Receive(id, Actor, request.Quantities ?? [], request.Note));

    [HttpGet("inventory")]
    public IReadOnlyList<InventoryBook> Inventory() => receiving.Inventory();

    [HttpGet("movements")]
    public IReadOnlyList<StockMovement> Movements(int? bookId) => receiving.Movements(bookId);

    [HttpPut("inventory/{bookId:int:min(1)}/minimum")]
    public IActionResult SetMinimum(int bookId, MinimumRequest request) =>
        Run(() => receiving.SetMinimum(bookId, request.Minimum));

    [HttpPost("inventory/{bookId:int:min(1)}/reconcile")]
    public IActionResult Reconcile(int bookId, ReconcileRequest request) =>
        Run(() => receiving.Reconcile(bookId, request.ExpectedStock, request.Count, request.Reason, Actor));

    private IActionResult SaveSupplier(int id, SupplierRequest request) =>
        Run(() => receiving.SaveSupplier(id, request.Name, request.Phone, request.Email, request.Address));

    private IActionResult Run(Action operation)
    {
        try
        {
            operation();
            return NoContent();
        }
        catch (ReceivingRuleException exception)
        {
            return RuleError(exception.Message);
        }
    }

    private ObjectResult RuleError(string detail) => Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);

    public sealed record SupplierRequest(string? Name, string? Phone, string? Email, string? Address);
    public sealed record CreatePurchaseRequest(int SupplierId, Dictionary<int, PurchaseInput>? Lines);
    public sealed record ReceivePurchaseRequest(Dictionary<int, int>? Quantities, string? Note);
    public sealed record MinimumRequest([property: JsonRequired] int Minimum);
    public sealed record ReconcileRequest([property: JsonRequired] int ExpectedStock,
        [property: JsonRequired] int Count, string? Reason);
}
