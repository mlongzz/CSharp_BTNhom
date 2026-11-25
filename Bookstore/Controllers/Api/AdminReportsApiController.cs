using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/admin/reports")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AccountRoles.Admin)]
public sealed class AdminReportsApiController(IBookstoreReports reports, TimeProvider clock) : ControllerBase
{
    private const string ExcelMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet]
    public ActionResult<BookstoreReport> Read([FromQuery] DateOnly? from, [FromQuery] DateOnly? through)
    {
        if (from is null || through is null)
            return Problem(detail: "Vui lòng nhập đầy đủ khoảng ngày báo cáo.", statusCode: 400);
        if (from > through)
            return Problem(detail: "Đến ngày phải bằng hoặc sau Từ ngày.", statusCode: 400);

        return reports.Read(from.Value, through.Value, Today());
    }

    [HttpGet("export")]
    public IActionResult Export([FromQuery] DateOnly? from, [FromQuery] DateOnly? through,
        [FromQuery] ReportSection section = ReportSection.Overview)
    {
        if (from is null || through is null)
            return Problem(detail: "Vui lòng nhập đầy đủ khoảng ngày báo cáo.", statusCode: 400);
        if (from > through)
            return Problem(detail: "Đến ngày phải bằng hoặc sau Từ ngày.", statusCode: 400);
        if (!Enum.IsDefined(section))
            return Problem(detail: "Loại báo cáo không hợp lệ.", statusCode: 400);

        var report = reports.Read(from.Value, through.Value, Today());
        var model = new ReportsViewModel(from.Value, through.Value, section, report);
        return File(ReportWorkbook.Export(model, report), ExcelMediaType, $"Bao-cao-{from:yyyyMMdd}-{through:yyyyMMdd}.xlsx");
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
}
