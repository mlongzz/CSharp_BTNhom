
using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/admin/books")]
[Authorize(Roles = AccountRoles.Admin)]
[IgnoreAntiforgeryToken]
public sealed class AdminBooksApiController(
    CatalogService catalog) : ControllerBase
{
    // 1. Xem và tìm kiếm sách
    [HttpGet]
    public IActionResult GetBooks([FromQuery] string? q)
    {
        return Ok(catalog.ManageBooks(q));
    }

    // 2. Lấy thể loại, tác giả và nhà xuất bản
    [HttpGet("references")]
    public IActionResult GetReferences()
    {
        return Ok(catalog.References());
    }

    // 3. Thêm sách
    [HttpPost]
    public IActionResult Create(
        [FromBody] CatalogBookInput input)
    {
        if (input.Id is not null)
            return BadRequest(new
            {
                message = "Sách mới không được có ID."
            });

        try
        {
            int id = catalog.SaveBook(null, input.Draft());
            return Ok(new { id, message = "Đã thêm sách." });
        }
        catch (CatalogException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // 4. Cập nhật sách
    [HttpPut("{id:int}")]
    public IActionResult Update(
        int id, [FromBody] CatalogBookInput input)
    {
        if (id <= 0)
            return BadRequest();

        try
        {
            catalog.SaveBook(id, input.Draft());
            return Ok(new { id, message = "Đã cập nhật sách." });
        }
        catch (CatalogException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // 5. Xóa sách
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        try
        {
            catalog.DeleteBook(id);
            return NoContent();
        }
        catch (CatalogException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
