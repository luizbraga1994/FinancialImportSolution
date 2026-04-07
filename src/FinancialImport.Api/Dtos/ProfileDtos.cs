using System.ComponentModel.DataAnnotations;

namespace FinancialImport.Api.Dtos;

public sealed class ProfileDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyCollection<PermissionDto> Permissions { get; set; } = Array.Empty<PermissionDto>();
}

public sealed class CreateProfileRequest
{
    [Required] [MaxLength(80)] public string Name { get; set; } = string.Empty;
    [MaxLength(200)] public string? Description { get; set; }
    public List<long> PermissionIds { get; set; } = new();
}

public sealed class UpdateProfileRequest
{
    [Required] [MaxLength(80)] public string Name { get; set; } = string.Empty;
    [MaxLength(200)] public string? Description { get; set; }
    public bool IsActive { get; set; }
    public List<long> PermissionIds { get; set; } = new();
}
