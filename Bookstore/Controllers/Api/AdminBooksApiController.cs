
using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/admin/books")]
[Authorize(Roles = AccountRoles.Admin)]
public sealed class AdminBooksApiController(
    CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public IActionResult GetBooks(
        [FromQuery] string? q)
    {
        var books = catalog.ManageBooks(q);

        return Ok(books);
    }
}
