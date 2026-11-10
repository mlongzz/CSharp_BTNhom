using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = "Warehouse,Admin")]
public sealed class WarehouseController(ReceivingService receiving, ILogger<WarehouseController> logger) : Controller
{
    private string Actor => User.Identity!.Name!;

    [HttpGet]
    public IActionResult Index() => View(new WarehouseViewModel(receiving.Inventory(), receiving.Suppliers(), receiving.Purchases()));

    [HttpGet]
    public IActionResult Stock(int? bookId) => View(new InventoryViewModel(receiving.Inventory(), receiving.Movements(bookId), bookId));

    [HttpPost, Authorize(Roles = "Warehouse")]
    public IActionResult SaveSupplier(int id, string? name, string? phone, string? email, string? address) =>
        Run(() => receiving.SaveSupplier(id, name, phone, email, address), "Đã lưu nhà cung cấp.");

    [HttpPost, Authorize(Roles = "Warehouse")]
    public IActionResult DeleteSupplier(int id) =>
        Run(() => receiving.DeleteSupplier(id), "Đã xóa nhà cung cấp.");

    [HttpPost, Authorize(Roles = "Warehouse")]
    public IActionResult Create(int supplierId, [Bind(Prefix = "quantities")] Dictionary<int, int>? quantities,
        [Bind(Prefix = "prices")] Dictionary<int, int>? prices) =>
        Run(() =>
        {
            var lines = (quantities ?? []).Where(item => item.Value != 0).ToDictionary(item => item.Key,
                item => new PurchaseInput(item.Value, prices?.GetValueOrDefault(item.Key) ?? 0));
            var id = receiving.Create(Actor, supplierId, lines);
            logger.LogInformation("Purchase {PurchaseId} created by {Actor}", id, Actor);
        }, "Đã lập phiếu và chuyển sang chờ duyệt.");

    [HttpPost, Authorize(Roles = "Admin")]
    public IActionResult Approve(long id) => Run(() =>
    {
        receiving.Approve(id, Actor);
        logger.LogInformation("Purchase {PurchaseId} approved by {Actor}", id, Actor);
    }, "Đã duyệt phiếu nhập.");

    [HttpPost, Authorize(Roles = "Warehouse")]
    public IActionResult Receive(long id, [Bind(Prefix = "quantities")] Dictionary<int, int>? quantities, string? note) =>
        Run(() =>
        {
            receiving.Receive(id, Actor, quantities ?? [], note);
            logger.LogInformation("Purchase {PurchaseId} received by {Actor}", id, Actor);
        }, "Đã xác nhận thực nhận và cập nhật tồn kho.");

    [HttpPost, Authorize(Roles = "Warehouse")]
    public IActionResult SetMinimum(int bookId, int minimum) =>
        Run(() => receiving.SetMinimum(bookId, minimum), "Đã cập nhật ngưỡng cảnh báo.", nameof(Stock));

    [HttpPost, Authorize(Roles = "Warehouse")]
    public IActionResult Reconcile(int bookId, int count, string? reason) =>
        Run(() => receiving.Reconcile(bookId, count, reason, Actor), "Đã ghi nhận kết quả kiểm kê.", nameof(Stock));

    private IActionResult Run(Action operation, string success, string destination = nameof(Index))
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu biểu mẫu không hợp lệ.";
            return RedirectToAction(destination);
        }

        try
        {
            operation();
            TempData["Success"] = success;
        }
        catch (ReceivingRuleException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToAction(destination);
    }
}
