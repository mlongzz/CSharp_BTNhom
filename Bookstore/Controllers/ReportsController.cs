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
        View(Load(from, through, section));

    [HttpGet]
    public IActionResult Export(DateOnly? from, DateOnly? through, ReportSection section)
    {
        var model = Load(from, through, section);
        if (model.Report is not { } report)
            return View(nameof(Index), model);

        return File(ReportWorkbook.Export(model, report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Bao-cao-{model.From:yyyyMMdd}-{model.Through:yyyyMMdd}.xlsx");
    }

    private ReportsViewModel Load(DateOnly? from, DateOnly? through, ReportSection section)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = through ?? today;
        if (!Enum.IsDefined(section))
            ModelState.AddModelError(nameof(section), "Loại báo cáo không hợp lệ.");
        if (start > end)
            ModelState.AddModelError(nameof(through), "Đến ngày phải bằng hoặc sau Từ ngày.");

        return new(start, end, section, ModelState.IsValid ? reports.Read(start, end, today) : null);
    }
}
