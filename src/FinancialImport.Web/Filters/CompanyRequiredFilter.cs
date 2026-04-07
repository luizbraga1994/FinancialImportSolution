using FinancialImport.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FinancialImport.Web.Filters;

public sealed class CompanyRequiredFilter : IAsyncActionFilter
{
    private readonly ICompanyContext _companyContext;

    public CompanyRequiredFilter(ICompanyContext companyContext)
    {
        _companyContext = companyContext;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        var controller = context.ActionDescriptor.RouteValues["controller"];
        if (string.Equals(controller, "Company", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        if (string.IsNullOrWhiteSpace(_companyContext.CompanyDb))
        {
            context.Result = new RedirectToActionResult("Select", "Company", null);
            return;
        }

        await next();
    }
}
