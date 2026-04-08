using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Domain.Constants;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Api.Controllers;

[ApiController]
[Route("api/v1/imports")]
[Authorize(Policy = PermissionCodes.ImportarLancamentos)]
public sealed class ImportApiController : ControllerBase
{
    private readonly IImportService _importService;
    private readonly IImportRepository _importRepository;
    private readonly ICompanyContext _companyContext;
    private readonly AppDbContext _dbContext;
    private readonly IHashService _hashService;
    private readonly ILogger<ImportApiController> _logger;

    public ImportApiController(
        IImportService importService,
        IImportRepository importRepository,
        ICompanyContext companyContext,
        AppDbContext dbContext,
        IHashService hashService,
        ILogger<ImportApiController> logger)
    {
        _importService = importService;
        _importRepository = importRepository;
        _companyContext = companyContext;
        _dbContext = dbContext;
        _hashService = hashService;
        _logger = logger;
    }

    [HttpPost("preview")]
    public async Task<ActionResult<ApiResponse<ImportPreviewResponse>>> Preview(
        IFormFile file,
        [FromForm] string? layout = null,
        [FromForm] string? branchDefault = null,
        [FromForm] bool useBranchFromFile = true,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<ImportPreviewResponse>.Fail("Arquivo nao fornecido."));

        const long maxFileSize = 10 * 1024 * 1024; // 10 MB
        if (file.Length > maxFileSize)
            return BadRequest(ApiResponse<ImportPreviewResponse>.Fail("Arquivo excede o tamanho maximo de 10 MB."));

        var allowedExtensions = new[] { ".csv", ".txt", ".xlsx" };
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
            return BadRequest(ApiResponse<ImportPreviewResponse>.Fail("Extensao de arquivo nao permitida. Use CSV, TXT ou XLSX."));

        var context = await ReadFileAsync(file, cancellationToken);
        if (!string.IsNullOrWhiteSpace(layout)) context.DetectedLayout = layout;
        context.BranchDefault = branchDefault;
        context.UseBranchFromFile = useBranchFromFile;

        var result = await _importService.PreviewAsync(context, cancellationToken);

        return Ok(ApiResponse<ImportPreviewResponse>.Ok(new ImportPreviewResponse
        {
            ImportFileId = result.ImportFileId,
            LayoutDetected = result.LayoutDetected,
            TotalLines = result.Lines.Count,
            ValidLines = result.Lines.Count - result.Errors.Count,
            InvalidLines = result.Errors.Count,
            Errors = result.Errors
        }));
    }

    [HttpPost("{importFileId:long}/confirm")]
    public async Task<ActionResult<ApiResponse<ImportProcessResponse>>> Confirm(
        long importFileId,
        CancellationToken cancellationToken)
    {
        var result = await _importService.ProcessAsync(importFileId, cancellationToken);

        return Ok(ApiResponse<ImportProcessResponse>.Ok(new ImportProcessResponse
        {
            ImportFileId = result.ImportFileId,
            Imported = result.Imported,
            Duplicated = result.Duplicated,
            Invalid = result.Invalid,
            SapErrors = result.SapErrors
        }));
    }

    [HttpPost("{importFileId:long}/reprocess")]
    [Authorize(Policy = PermissionCodes.ReprocessarImportacao)]
    public async Task<ActionResult<ApiResponse<ImportProcessResponse>>> Reprocess(
        long importFileId,
        CancellationToken cancellationToken)
    {
        var result = await _importService.ProcessAsync(importFileId, cancellationToken);

        return Ok(ApiResponse<ImportProcessResponse>.Ok(new ImportProcessResponse
        {
            ImportFileId = result.ImportFileId,
            Imported = result.Imported,
            Duplicated = result.Duplicated,
            Invalid = result.Invalid,
            SapErrors = result.SapErrors
        }));
    }

    [HttpGet("history")]
    [Authorize(Policy = PermissionCodes.VisualizarHistorico)]
    public async Task<ActionResult<ApiResponse<PagedResult<ImportHistoryDto>>>> History(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? companyDb = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);

        var query = _dbContext.ImportFiles.AsNoTracking().AsQueryable();

        var currentCompany = companyDb ?? _companyContext.CompanyDb;
        if (!string.IsNullOrWhiteSpace(currentCompany))
        {
            query = query.Where(f => f.CompanyDb == currentCompany);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(f => f.ImportedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new ImportHistoryDto
            {
                Id = f.Id,
                OriginalFileName = f.OriginalFileName,
                CompanyDb = f.CompanyDb,
                LayoutDetected = f.LayoutDetected,
                Status = f.Status.ToString(),
                TotalLines = f.TotalLines,
                ImportedLines = f.ImportedLines,
                InvalidLines = f.InvalidLines,
                DuplicatedLines = f.DuplicatedLines,
                LinesWithError = f.LinesWithError,
                ImportedAt = f.ImportedAt,
                UserId = f.UserId
            })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<PagedResult<ImportHistoryDto>>.Ok(new PagedResult<ImportHistoryDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        }));
    }

    [HttpGet("{importFileId:long}/lines")]
    [Authorize(Policy = PermissionCodes.VisualizarHistorico)]
    public async Task<ActionResult<ApiResponse<PagedResult<ImportLineDto>>>> Lines(
        long importFileId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = _dbContext.ImportLines
            .AsNoTracking()
            .Where(l => l.ImportFileId == importFileId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new ImportLineDto
            {
                Id = l.Id,
                Reference = l.Reference,
                AccountCode = l.AccountCode,
                ContraAccountCode = l.ContraAccountCode,
                PostingDate = l.PostingDate,
                Amount = l.Amount,
                CreditAmount = l.CreditAmount,
                DebitAmount = l.DebitAmount,
                LineMemo = l.LineMemo,
                BranchCode = l.BranchCode,
                Status = l.Status.ToString(),
                ValidationMessage = l.ValidationMessage,
                SapReturnMessage = l.SapReturnMessage,
                SapDocEntry = l.SapDocEntry
            })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<PagedResult<ImportLineDto>>.Ok(new PagedResult<ImportLineDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        }));
    }

    private static async Task<ImportFileContext> ReadFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
        }

        if (lines.Count == 0)
        {
            return new ImportFileContext { FileName = file.FileName, FileBytes = fileBytes };
        }

        var delimiter = lines[0].Contains(';') ? ';' : ',';
        var headers = lines[0].Split(delimiter).Select(h => h.Trim()).ToArray();
        var rows = new List<ImportRow>();

        foreach (var line in lines.Skip(1))
        {
            var values = line.Split(delimiter);
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                var value = i < values.Length ? values[i].Trim() : null;
                dict[headers[i]] = string.IsNullOrWhiteSpace(value) ? null : value;
            }
            rows.Add(new ImportRow(dict));
        }

        return new ImportFileContext
        {
            FileName = file.FileName,
            FileBytes = fileBytes,
            Headers = headers,
            Rows = rows
        };
    }
}
