using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Onboarding;

/// <summary>
/// Request to update the signed-in user's public account types.
/// </summary>
public sealed class UpdateOnboardingAccountTypesRequest
{
    /// <summary>
    /// One or more public account types, such as Parent, Player, Coach, TeamManager, AcademyDirector, or OrganizationAdmin.
    /// </summary>
    [MinLength(1)]
    public IReadOnlyCollection<string> AccountTypes { get; set; } = [];
}
