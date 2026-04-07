using FinancialImport.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize(Policy = PermissionCodes.GerenciarPermissoes)]
public sealed class AdminPermissionsController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
