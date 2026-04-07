using FinancialImport.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize(Policy = PermissionCodes.GerenciarUsuarios)]
public sealed class AdminUsersController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
