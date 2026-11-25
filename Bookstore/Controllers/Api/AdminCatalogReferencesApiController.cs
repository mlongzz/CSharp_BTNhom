using Bookstore.Application;
using Bookstore.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Controllers.Api;

[ApiController]
[Route("api/admin/catalog/references")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AccountRoles.Admin)]
[IgnoreAntiforgeryToken]
public sealed class AdminCatalogReferencesApiController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public CatalogReferences GetReferences() => catalog.References();

    [HttpPost("{kind}")]
    public IActionResult CreateReference(ReferenceKind kind, CatalogReferenceDraft draft) =>
        Change(kind, () => catalog.SaveReference(kind, null, draft));

    [HttpPut("{kind}/{id:int}")]
    public IActionResult UpdateReference(ReferenceKind kind, int id, CatalogReferenceDraft draft) =>
        Change(kind, () => catalog.SaveReference(kind, id, draft));

    [HttpDelete("{kind}/{id:int}")]
    public IActionResult DeleteReference(ReferenceKind kind, int id) =>
        Change(kind, () => catalog.DeleteReference(kind, id));

    private IActionResult Change(ReferenceKind kind, Action change)
    {
        if (!Enum.IsDefined(kind))
            return Problem(detail: "Loại danh mục không hợp lệ.", statusCode: StatusCodes.Status400BadRequest);

        try
        {
            change();
            return NoContent();
        }
        catch (CatalogException exception)
        {
            return Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
