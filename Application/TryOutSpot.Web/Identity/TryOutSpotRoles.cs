using Microsoft.AspNetCore.Identity;

namespace TryOutSpot.Web.Identity;

public static class TryOutSpotRoles
{
    public const string Parent = "Parent";
    public const string Player = "Player";
    public const string TeamRepresentative = "TeamRepresentative";
    public const string Coach = "Coach";
    public const string TeamManager = "TeamManager";
    public const string AcademyDirector = "AcademyDirector";
    public const string OrganizationAdmin = "OrganizationAdmin";
    public const string PlatformAdmin = "PlatformAdmin";

    public static readonly string[] PublicRegistrationRoles =
    [
        Parent,
        Player,
        TeamRepresentative
    ];

    public static readonly string[] LegacyTeamBundleRoles =
    [
        Coach,
        TeamManager,
        AcademyDirector,
        OrganizationAdmin
    ];

    public static readonly string[] PublicOrLegacyRegistrationRoles =
    [
        .. PublicRegistrationRoles,
        .. LegacyTeamBundleRoles
    ];

    public static readonly string[] AllRoles =
    [
        Parent,
        Player,
        TeamRepresentative,
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
        CreateRole("88888888-8888-8888-8888-888888888888", TeamRepresentative),
        CreateRole("33333333-3333-3333-3333-333333333333", Coach),
        CreateRole("44444444-4444-4444-4444-444444444444", TeamManager),
        CreateRole("55555555-5555-5555-5555-555555555555", AcademyDirector),
        CreateRole("66666666-6666-6666-6666-666666666666", OrganizationAdmin),
        CreateRole("77777777-7777-7777-7777-777777777777", PlatformAdmin)
    ];

    public static readonly string[] PlayerParentBundleRoles =
    [
        Parent,
        Player
    ];

    public static string? NormalizePublicRegistrationRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var normalized = role.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        return normalized.ToLowerInvariant() switch
        {
            "parent" => Parent,
            "player" => Player,
            "teamrepresentative" or "teamrep" or "coach" or "teammanager" or "academydirector" or "organizationadmin" =>
                TeamRepresentative,
            _ => null
        };
    }

    public static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var normalizedPublicRole = NormalizePublicRegistrationRole(role);
        if (normalizedPublicRole is not null)
        {
            return normalizedPublicRole;
        }

        return string.Equals(role.Trim(), PlatformAdmin, StringComparison.OrdinalIgnoreCase)
            ? PlatformAdmin
            : null;
    }

    public static bool IsPlayerParentRole(string? role)
    {
        return role is not null
            && PlayerParentBundleRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsTeamBundleRole(string? role)
    {
        return string.Equals(role, TeamRepresentative, StringComparison.OrdinalIgnoreCase)
            || IsLegacyTeamBundleRole(role);
    }

    public static bool IsLegacyTeamBundleRole(string? role)
    {
        return role is not null
            && LegacyTeamBundleRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public static string? SelectHighestPrecedenceTeamRole(IEnumerable<string> roles)
    {
        return roles.Any(IsTeamBundleRole)
            ? TeamRepresentative
            : null;
    }

    public static bool TryValidateSingleRolePerBundle(
        IReadOnlyCollection<string> requestedRoles,
        out string? validationError)
    {
        var normalized = requestedRoles
            .Select(NormalizePublicRegistrationRole)
            .Where(role => role is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var playerParentCount = normalized.Count(IsPlayerParentRole);
        if (playerParentCount > 1)
        {
            validationError = "Select only one role in the Parent/Player bundle (Parent or guardian OR Player).";
            return false;
        }

        var teamCount = normalized.Count(IsTeamBundleRole);
        if (teamCount > 1)
        {
            validationError = "Select only one team role (Team representative).";
            return false;
        }

        validationError = null;
        return true;
    }

    public static string[] CanonicalizeRoleSet(
        IEnumerable<string> roles,
        bool includePlatformAdmin = true)
    {
        var normalizedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            var normalizedPublicRole = NormalizePublicRegistrationRole(role);
            if (normalizedPublicRole is not null)
            {
                normalizedRoles.Add(normalizedPublicRole);
                continue;
            }

            if (includePlatformAdmin
                && string.Equals(role, PlatformAdmin, StringComparison.OrdinalIgnoreCase))
            {
                normalizedRoles.Add(PlatformAdmin);
            }
        }

        return normalizedRoles
            .OrderBy(GetRoleSortOrder)
            .ThenBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetRoleSortOrder(string role)
    {
        if (string.Equals(role, Parent, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(role, Player, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(role, TeamRepresentative, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(role, PlatformAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 99;
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
