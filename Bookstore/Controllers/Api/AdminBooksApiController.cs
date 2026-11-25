using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/admin/books")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AccountRoles.Admin)]
[IgnoreAntiforgeryToken]
public sealed class AdminBooksApiController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<CatalogBook> GetBooks([FromQuery] string? q) => catalog.ManageBooks(q);

    [HttpGet("{id:int}")]
    public ActionResult<CatalogBook> GetBook(int id) => catalog.FindBook(id) is { } book ? book : NotFound();

    [HttpPost]
    public IActionResult CreateBook(CatalogBookDraft draft) => Change(() =>
    {
        int id = catalog.SaveBook(null, draft);
        return CreatedAtAction(nameof(GetBook), new { id }, new { id });
    });

    [HttpPut("{id:int}")]
    public IActionResult UpdateBook(int id, CatalogBookDraft draft) => Change(() =>
    {
        catalog.SaveBook(id, draft);
        return NoContent();
    });

    [HttpPut("{id:int}/availability")]
    public IActionResult SetAvailability(int id, BookAvailability input)
    {
        if (input.Active is not bool active)
            return Problem(detail: "Chọn trạng thái kinh doanh.", statusCode: StatusCodes.Status400BadRequest);

        return Change(() =>
        {
            catalog.SetBookActive(id, active);
            return NoContent();
        });
    }

    [HttpDelete("{id:int}")]
    public IActionResult DeleteBook(int id) => Change(() =>
    {
        catalog.DeleteBook(id);
        return NoContent();
    });

    private IActionResult Change(Func<IActionResult> change)
    {
        try
        {
            return change();
        }
        catch (CatalogException exception)
        {
            return Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    public sealed record BookAvailability(bool? Active);
}
