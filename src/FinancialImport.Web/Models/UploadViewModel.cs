using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace FinancialImport.Web.Models;

public sealed class UploadViewModel
{
    [Required]
    public IFormFile? File { get; set; }

    public string? Layout { get; set; }

    public string? BranchDefault { get; set; }

    public bool UseBranchFromFile { get; set; } = true;
}
