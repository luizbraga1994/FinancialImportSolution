using FinancialImport.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize(Policy = PermissionCodes.VisualizarHistorico)]
public sealed class HistoryController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
