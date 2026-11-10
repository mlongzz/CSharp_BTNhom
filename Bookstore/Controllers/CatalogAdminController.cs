using Bookstore.Application;
using Bookstore.Domain;
using Bookstore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers;

[Authorize(Roles = "Admin")]
public sealed class CatalogAdminController(CatalogService catalog) : Controller
{
    [HttpGet]
    public IActionResult Index(string? q, int? editId)
    {
        var book = editId is int id ? catalog.FindBook(id) : null;
        if (editId is not null && book is null)
            return NotFound();

        return View(BookPage(book is null ? new() : CatalogBookInput.From(book), q));
    }

    [HttpPost]
    public IActionResult SaveBook([Bind(Prefix = "Input")] CatalogBookInput input)
    {
        try
        {
            if (!ModelState.IsValid)
                throw new CatalogException("Kiểm tra các trường dữ liệu sách.");
            catalog.SaveBook(input.Id, input.Draft());
            TempData["Success"] = "Đã lưu sách.";
            return RedirectToAction(nameof(Index));
        }
        catch (CatalogException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(nameof(Index), BookPage(input, null));
        }
    }

    [HttpPost]
    public IActionResult DeleteBook(int id)
    {
        try
        {
            catalog.DeleteBook(id);
            TempData["Success"] = "Đã xóa sách.";
        }
        catch (CatalogException exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult References(ReferenceKind kind = ReferenceKind.Category, int? editId = null)
    {
        if (!Enum.IsDefined(kind))
            return BadRequest();

        var references = ReferenceList(kind);
        var item = editId is int id ? references.SingleOrDefault(reference => reference.Id == id) : null;
        if (editId is not null && item is null)
            return NotFound();

        return View(new CatalogReferencePage(kind, references, item is null ? new() : new()
        {
            Id = item.Id, Name = item.Name, Description = item.Description, ParentId = item.ParentId
        }));
    }

    [HttpPost]
    public IActionResult SaveReference(ReferenceKind kind, [Bind(Prefix = "Input")] CatalogReferenceInput input)
    {
        if (!Enum.IsDefined(kind))
            return BadRequest();

        try
        {
            if (!ModelState.IsValid)
                throw new CatalogException("Kiểm tra dữ liệu danh mục.");
            catalog.SaveReference(kind, input.Id, new(input.Name ?? "", input.Description, input.ParentId));
            TempData["Success"] = "Đã lưu bản ghi.";
            return RedirectToAction(nameof(References), new { kind });
        }
        catch (CatalogException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(nameof(References), new CatalogReferencePage(kind, ReferenceList(kind), input));
        }
    }

    [HttpPost]
    public IActionResult DeleteReference(ReferenceKind kind, int id)
    {
        if (!Enum.IsDefined(kind) || !ModelState.IsValid)
            return BadRequest();

        try
        {
            catalog.DeleteReference(kind, id);
            TempData["Success"] = "Đã xóa bản ghi.";
        }
        catch (CatalogException exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(References), new { kind });
    }

    private CatalogAdminPage BookPage(CatalogBookInput input, string? query) =>
        new(catalog.ManageBooks(query), catalog.References(), input, query);

    private IReadOnlyList<CatalogReference> ReferenceList(ReferenceKind kind)
    {
        var references = catalog.References();
        return kind switch
        {
            ReferenceKind.Category => references.Categories,
            ReferenceKind.Author => references.Authors,
            ReferenceKind.Publisher => references.Publishers,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
