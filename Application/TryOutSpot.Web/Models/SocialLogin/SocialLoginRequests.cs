using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.SocialLogin;

public sealed class SocialLoginRegisterRequest
{
    [Required]
    public string ExternalLoginToken { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [Phone]
    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [MinLength(1)]
    public IReadOnlyCollection<string> AccountTypes { get; set; } = [];
}

public sealed class SocialLoginLinkRequest
{
    [Required]
    public string ExternalLoginToken { get; set; } = string.Empty;
}

public sealed class SocialLoginUnlinkRequest
{
    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;
}
