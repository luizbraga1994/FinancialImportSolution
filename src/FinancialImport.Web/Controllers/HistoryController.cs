using FinancialImport.Application.Abstractions;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class HistoryController : Controller
{
    private readonly AppDbContext _dbContext;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(AppDbContext dbContext, ICompanyContext companyContext, ILogger<HistoryController> logger)
    {
        _dbContext = dbContext;
        _companyContext = companyContext;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var companyDb = _companyContext.CompanyDb;
        var userIdClaim = User.FindFirst("user_id")?.Value;
        var userId = long.TryParse(userIdClaim, out var uid) ? uid : (long?)null;

        _logger.LogInformation("=== HISTORICO ===");
        _logger.LogInformation("CompanyDb do contexto: '{CompanyDb}'", companyDb);
        _logger.LogInformation("UserId do claim: '{UserIdClaim}' (parsed: {UserId})", userIdClaim, userId);
        _logger.LogInformation("Claims disponiveis: {Claims}", string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));

        if (string.IsNullOrWhiteSpace(companyDb))
        {
            _logger.LogWarning("CompanyDb esta vazio. Tentando obter da sessao SAP ou claim.");

            // Tenta obter do claim novamente
            var claimCompany = User.FindFirst("company_db")?.Value;
            if (!string.IsNullOrWhiteSpace(claimCompany))
            {
                companyDb = claimCompany;
                _logger.LogInformation("CompanyDb obtido do claim 'company_db': '{CompanyDb}'", companyDb);
            }
        }

        if (string.IsNullOrWhiteSpace(companyDb))
        {
            _logger.LogWarning("Nenhuma empresa selecionada. Exibindo mensagem de erro.");
            TempData["Error"] = "Nenhuma empresa selecionada. Por favor, selecione uma empresa no menu Empresas.";
            return View(new List<ImportFile>());
        }

        var imports = await _dbContext.ImportFiles
            .AsNoTracking()
            .Include(f => f.User)
            .Where(f => f.CompanyDb == companyDb)
            .OrderByDescending(f => f.ImportedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Encontradas {Count} importacoes para a company '{CompanyDb}'.", imports.Count, companyDb);

        if (imports.Count == 0)
        {
            _logger.LogInformation("Nenhuma importacao encontrada para company '{CompanyDb}'. Verifique se existem registros na tabela ImportacaoArquivo.", companyDb);
        }

        return View(imports);
    }
}