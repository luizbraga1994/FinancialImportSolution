using FinancialImport.Application.Security;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthApiController : ControllerBase
{
    private readonly IApplicationAuthService _authService;
    private readonly JwtTokenService _jwtTokenService;
    private readonly ILogger<AuthApiController> _logger;

    public AuthApiController(
        IApplicationAuthService authService,
        JwtTokenService jwtTokenService,
        ILogger<AuthApiController> logger)
    {
        _authService = authService;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _authService.SignInAsync(request.Login, request.Password, cancellationToken);
        var token = _jwtTokenService.GenerateToken(session);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        var response = new LoginResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            UserId = session.UserId,
            Login = session.Login,
            Name = session.Name,
            IsGlobalAdmin = session.IsGlobalAdmin,
            Profiles = session.Profiles,
            Permissions = session.Permissions,
            AllowedCompanies = session.AllowedCompanies
        };

        return Ok(ApiResponse<LoginResponse>.Ok(response));
    }

    [Authorize]
    [HttpGet("me")]
    public ActionResult<ApiResponse<object>> Me()
    {
        var userId = User.FindFirst("user_id")?.Value;
        var login = User.FindFirst("login")?.Value;
        var name = User.FindFirst("name")?.Value;
        var isGlobalAdmin = User.FindFirst("global_admin")?.Value == "true";
        var permissions = User.FindAll("permission").Select(c => c.Value).ToArray();
        var companies = User.FindAll("company").Select(c => c.Value).ToArray();

        return Ok(ApiResponse<object>.Ok(new
        {
            UserId = userId,
            Login = login,
            Name = name,
            IsGlobalAdmin = isGlobalAdmin,
            Permissions = permissions,
            AllowedCompanies = companies
        }));
    }
}
