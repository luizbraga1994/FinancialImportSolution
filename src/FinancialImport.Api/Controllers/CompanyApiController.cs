using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Sap;
using FinancialImport.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Api.Controllers;

[ApiController]
[Route("api/v1/companies")]
[Authorize]
public sealed class CompanyApiController : ControllerBase
{
    private readonly ISapCompanyDiscoveryService _companyDiscovery;
    private readonly ISapCompanySessionService _sapSessionService;
    private readonly IUserContext _userContext;

    public CompanyApiController(
        ISapCompanyDiscoveryService companyDiscovery,
        ISapCompanySessionService sapSessionService,
        IUserContext userContext)
    {
        _companyDiscovery = companyDiscovery;
        _sapSessionService = sapSessionService;
        _userContext = userContext;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<CompanyDto>>>> List(CancellationToken cancellationToken)
    {
        var companies = await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken);
        var isGlobalAdmin = User.FindFirst("global_admin")?.Value == "true";
        var allowed = User.FindAll("company").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = companies
            .Where(c => c.IsActive)
            .Where(c => isGlobalAdmin || allowed.Count == 0 || allowed.Contains(c.CompanyDb))
            .Select(c => new CompanyDto
            {
                CompanyDb = c.CompanyDb,
                CompanyName = c.CompanyName,
                Server = c.Server,
                IsActive = c.IsActive
            })
            .ToArray();

        return Ok(ApiResponse<IReadOnlyCollection<CompanyDto>>.Ok(filtered));
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<CompanyLoginResponse>>> LoginCompany(
        [FromBody] CompanyLoginRequest request,
        CancellationToken cancellationToken)
    {
        var isGlobalAdmin = User.FindFirst("global_admin")?.Value == "true";
        var allowed = User.FindAll("company").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!isGlobalAdmin && allowed.Count > 0 && !allowed.Contains(request.CompanyDb))
        {
            return Forbid();
        }

        var result = await _sapSessionService.SignInCompanyAsync(
            request.CompanyDb, request.SapUserName, request.SapPassword, cancellationToken);

        if (!result.Success || result.Session == null)
        {
            return BadRequest(ApiResponse<CompanyLoginResponse>.Fail(result.ErrorMessage ?? "Falha ao autenticar no SAP."));
        }

        return Ok(ApiResponse<CompanyLoginResponse>.Ok(new CompanyLoginResponse
        {
            CompanyDb = result.Session.CompanyDb,
            CompanyName = result.Session.CompanyName,
            SapUserName = result.Session.SapUserName,
            ExpiresAt = result.Session.ExpiresAt
        }));
    }

    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse>> LogoutCompany(CancellationToken cancellationToken)
    {
        await _sapSessionService.SignOutCompanyAsync(cancellationToken);
        return Ok(ApiResponse.Ok());
    }

    [HttpGet("session")]
    public async Task<ActionResult<ApiResponse<CompanySessionDto?>>> GetSession(CancellationToken cancellationToken)
    {
        var session = await _sapSessionService.GetCurrentSessionAsync(cancellationToken);
        if (session == null)
        {
            return Ok(ApiResponse<CompanySessionDto?>.Ok(null));
        }

        return Ok(ApiResponse<CompanySessionDto?>.Ok(new CompanySessionDto
        {
            CompanyDb = session.CompanyDb,
            CompanyName = session.CompanyName,
            SapUserName = session.SapUserName,
            ExpiresAt = session.ExpiresAt
        }));
    }
}
