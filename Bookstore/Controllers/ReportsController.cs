using Bookstore.Application;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = "Admin")]
public sealed class ReportsController(IBookstoreReports reports, TimeProvider clock) : Controller
{
    [HttpGet]
    public IActionResult Index(DateOnly? from, DateOnly? through, ReportSection section) =>
        TryLoad(from, through, section, out var model) ? View(model) : BadRequest("Khoảng ngày báo cáo không hợp lệ.");

    [HttpGet]
    public IActionResult Export(DateOnly? from, DateOnly? through, ReportSection section)
    {
        if (!TryLoad(from, through, section, out var model))
            return BadRequest("Khoảng ngày báo cáo không hợp lệ.");

        return File(ReportWorkbook.Export(model!), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Bao-cao-{model!.From:yyyyMMdd}-{model.Through:yyyyMMdd}.xlsx");
    }

    private bool TryLoad(DateOnly? from, DateOnly? through, ReportSection section, out ReportsViewModel? model)
    {
        model = null;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = through ?? today;
        if (!ModelState.IsValid || !Enum.IsDefined(section) || start > end)
            return false;

        model = new(start, end, section, reports.Read(start, end, today));
        return true;
    }
}
