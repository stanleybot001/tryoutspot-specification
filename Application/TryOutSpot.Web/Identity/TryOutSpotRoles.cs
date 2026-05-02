using Microsoft.AspNetCore.Identity;

namespace TryOutSpot.Web.Identity;

public static class TryOutSpotRoles
{
    public const string Parent = "Parent";
    public const string Player = "Player";
    public const string Coach = "Coach";
    public const string TeamManager = "TeamManager";
    public const string AcademyDirector = "AcademyDirector";
    public const string OrganizationAdmin = "OrganizationAdmin";
    public const string PlatformAdmin = "PlatformAdmin";

    public static readonly string[] PublicRegistrationRoles =
    [
        Parent,
        Player,
        Coach,
        TeamManager,
        AcademyDirector,
        OrganizationAdmin
    ];

    public static readonly string[] AllRoles =
    [
        Parent,
        Player,
        Coach,
        TeamManager,
        AcademyDirector,
        OrganizationAdmin,
        PlatformAdmin
    ];

    public static readonly IdentityRole<Guid>[] SeedRoles =
    [
        CreateRole("11111111-1111-1111-1111-111111111111", Parent),
        CreateRole("22222222-2222-2222-2222-222222222222", Player),
        CreateRole("33333333-3333-3333-3333-333333333333", Coach),
        CreateRole("44444444-4444-4444-4444-444444444444", TeamManager),
        CreateRole("55555555-5555-5555-5555-555555555555", AcademyDirector),
        CreateRole("66666666-6666-6666-6666-666666666666", OrganizationAdmin),
        CreateRole("77777777-7777-7777-7777-777777777777", PlatformAdmin)
    ];

    public static string? NormalizePublicRegistrationRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return PublicRegistrationRoles.FirstOrDefault(knownRole =>
            string.Equals(knownRole, role.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return AllRoles.FirstOrDefault(knownRole =>
            string.Equals(knownRole, role.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static IdentityRole<Guid> CreateRole(string id, string name)
    {
        return new IdentityRole<Guid>
        {
            Id = Guid.Parse(id),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            ConcurrencyStamp = id
        };
    }
}
