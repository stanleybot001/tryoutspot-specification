using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.UserManagement;

public sealed class CreateManagedUserRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

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

    public bool EmailConfirmed { get; set; }

    public bool PhoneNumberConfirmed { get; set; }
}

public sealed class UpdateManagedUserProfileRequest
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

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
}

public sealed class UpdateManagedUserAccountTypesRequest
{
    [MinLength(1)]
    public IReadOnlyCollection<string> AccountTypes { get; set; } = [];
}

public sealed class SetManagedUserVerificationRequest
{
    public bool EmailConfirmed { get; set; }

    public bool PhoneNumberConfirmed { get; set; }
}

public sealed class ManagedUserStateChangeRequest
{
    [MaxLength(500)]
    public string? Reason { get; set; }
}
