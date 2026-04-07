using FinancialImport.Application.Abstractions;
using FinancialImport.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize]
public sealed class DashboardController : Controller
{
    private readonly IUserContext _userContext;
    private readonly ICompanyContext _companyContext;

    public DashboardController(IUserContext userContext, ICompanyContext companyContext)
    {
        _userContext = userContext;
        _companyContext = companyContext;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var model = new DashboardViewModel
        {
            UserName = _userContext.Name ?? _userContext.Login ?? "",
            CompanyDb = _companyContext.CompanyDb,
            CompanyName = _companyContext.CompanyName
        };

        return View(model);
    }
}
