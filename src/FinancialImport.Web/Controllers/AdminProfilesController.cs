using FinancialImport.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize(Policy = PermissionCodes.GerenciarPerfis)]
public sealed class AdminProfilesController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
