using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Dashboard;

namespace TryOutSpot.Web.Services;

public sealed class ActivationAssistanceService(
    AppDbContext dbContext,
    UserManager<User> userManager,
    IOptions<ActivationAssistanceOptions> options) : IActivationAssistanceService
{
    private const string DismissedEventType = "dismissed";

    private readonly ActivationAssistanceOptions assistanceOptions = options.Value;

    public async Task<ActivationAssistancePromptResponse> GetTeamFirstListingPromptAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not { IsActive: true } || !user.EmailConfirmed)
        {
            return EmptyPrompt();
        }

        if (user.CreatedAt > now.AddHours(-Math.Max(0, assistanceOptions.SignupGraceHours)))
        {
            return EmptyPrompt();
        }

        var roles = TryOutSpotRoles.CanonicalizeRoleSet(await userManager.GetRolesAsync(user));
        if (!roles.Any(TryOutSpotRoles.IsTeamBundleRole))
        {
            return EmptyPrompt();
        }

        var dismissedSince = now.AddDays(-Math.Max(1, assistanceOptions.DismissalCooldownDays));
        var wasRecentlyDismissed = await dbContext.ActivationAssistanceEvents
            .AsNoTracking()
            .AnyAsync(assistanceEvent =>
                    assistanceEvent.UserId == userId
                    && assistanceEvent.PromptKey == ActivationAssistancePromptKeys.TeamFirstListing
                    && assistanceEvent.EventType == DismissedEventType
                    && assistanceEvent.CreatedAt >= dismissedSince,
                cancellationToken);
        if (wasRecentlyDismissed)
        {
            return EmptyPrompt();
        }

        var managedTeams = await dbContext.UserTeamRoles
            .AsNoTracking()
            .Where(teamRole => teamRole.UserId == userId)
            .Where(teamRole => teamRole.IsActive)
            .Where(teamRole => teamRole.Team.IsActive)
            .Select(teamRole => new TeamActivationSnapshot(
                teamRole.TeamId,
                teamRole.Team.Name,
                teamRole.Team.TeamLevel,
                teamRole.Team.City,
                teamRole.Team.State,
                teamRole.Team.ZipCode,
                teamRole.Team.Email,
                teamRole.Team.PhoneNumber,
                teamRole.Team.IsSearchable,
                teamRole.Team.TeamSports.Count(teamSport => teamSport.IsActive),
                teamRole.Team.Opportunities.Count()))
            .ToArrayAsync(cancellationToken);

        if (managedTeams.Any(team => team.OpportunityCount > 0))
        {
            return EmptyPrompt();
        }

        if (managedTeams.Length == 0)
        {
            return BuildPrompt(
                TeamId: null,
                TeamName: null,
                Reasons:
                [
                    new("team_profile_missing", "Create a team profile")
                ],
                TeamProfileUrl: "/account/onboarding/add-team-or-organization",
                CreateListingUrl: null);
        }

        var incompleteTeam = managedTeams
            .Select(team => new
            {
                Team = team,
                Reasons = BuildIncompleteTeamReasons(team)
            })
            .Where(item => item.Reasons.Count > 0)
            .OrderBy(item => item.Team.TeamName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (incompleteTeam is null)
        {
            return EmptyPrompt();
        }

        return BuildPrompt(
            incompleteTeam.Team.TeamId,
            incompleteTeam.Team.TeamName,
            incompleteTeam.Reasons,
            $"/account/onboarding/team-opportunities/{incompleteTeam.Team.TeamId}/edit-profile",
            $"/account/onboarding/team-opportunities/{incompleteTeam.Team.TeamId}/new?type=tryout");
    }

    public async Task<ActivationAssistanceActionResponse> DismissTeamFirstListingPromptAsync(
        Guid userId,
        string? promptKey,
        Guid? teamId,
        CancellationToken cancellationToken)
    {
        var normalizedPromptKey = string.IsNullOrWhiteSpace(promptKey)
            ? ActivationAssistancePromptKeys.TeamFirstListing
            : promptKey.Trim();
        if (!string.Equals(normalizedPromptKey, ActivationAssistancePromptKeys.TeamFirstListing, StringComparison.Ordinal))
        {
            normalizedPromptKey = ActivationAssistancePromptKeys.TeamFirstListing;
        }

        var normalizedTeamId = teamId;
        if (normalizedTeamId.HasValue)
        {
            var ownsTeam = await dbContext.UserTeamRoles
                .AsNoTracking()
                .AnyAsync(teamRole =>
                        teamRole.UserId == userId
                        && teamRole.TeamId == normalizedTeamId.Value
                        && teamRole.IsActive,
                    cancellationToken);
            normalizedTeamId = ownsTeam ? normalizedTeamId : null;
        }

        var now = DateTime.UtcNow;
        dbContext.ActivationAssistanceEvents.Add(new ActivationAssistanceEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = normalizedTeamId,
            PromptKey = normalizedPromptKey,
            EventType = DismissedEventType,
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ActivationAssistanceActionResponse(now);
    }

    private ActivationAssistancePromptResponse BuildPrompt(
        Guid? TeamId,
        string? TeamName,
        IReadOnlyCollection<ActivationAssistanceReasonResponse> Reasons,
        string? TeamProfileUrl,
        string? CreateListingUrl)
    {
        var supportEmail = NormalizeOptional(assistanceOptions.SupportEmail);
        return new ActivationAssistancePromptResponse(
            true,
            ActivationAssistancePromptKeys.TeamFirstListing,
            TeamName is null
                ? "Need help getting your team ready?"
                : $"Need help getting {TeamName} ready?",
            "We noticed the team setup is not complete yet and no listing has been created. We can help finish the profile or walk through the first tryout listing.",
            TeamId,
            TeamName,
            Reasons,
            TeamProfileUrl,
            CreateListingUrl,
            supportEmail,
            BuildSupportEmailUrl(supportEmail, TeamName),
            NormalizeOptional(assistanceOptions.WhatsAppUrl));
    }

    private static IReadOnlyCollection<ActivationAssistanceReasonResponse> BuildIncompleteTeamReasons(
        TeamActivationSnapshot team)
    {
        var reasons = new List<ActivationAssistanceReasonResponse>();
        if (string.IsNullOrWhiteSpace(team.TeamLevel))
        {
            reasons.Add(new("team_level_missing", "Add age group or team level"));
        }

        if (team.SportCount == 0)
        {
            reasons.Add(new("team_sports_missing", "Choose baseball or softball"));
        }

        if (!HasLocation(team))
        {
            reasons.Add(new("team_location_missing", "Add city/state or ZIP code"));
        }

        if (string.IsNullOrWhiteSpace(team.Email) && string.IsNullOrWhiteSpace(team.PhoneNumber))
        {
            reasons.Add(new("team_contact_missing", "Add a team contact method"));
        }

        if (!team.IsSearchable)
        {
            reasons.Add(new("team_visibility_disabled", "Turn on team search visibility"));
        }

        return reasons;
    }

    private static bool HasLocation(TeamActivationSnapshot team)
    {
        return !string.IsNullOrWhiteSpace(team.ZipCode)
            || (!string.IsNullOrWhiteSpace(team.City) && !string.IsNullOrWhiteSpace(team.State));
    }

    private static ActivationAssistancePromptResponse EmptyPrompt()
    {
        return new ActivationAssistancePromptResponse(
            false,
            ActivationAssistancePromptKeys.TeamFirstListing,
            string.Empty,
            string.Empty,
            null,
            null,
            [],
            null,
            null,
            null,
            null,
            null);
    }

    private static string? BuildSupportEmailUrl(string? supportEmail, string? teamName)
    {
        if (string.IsNullOrWhiteSpace(supportEmail))
        {
            return null;
        }

        var subject = string.IsNullOrWhiteSpace(teamName)
            ? "Help posting my first TryOutSpot listing"
            : $"Help posting my first TryOutSpot listing for {teamName}";
        return $"mailto:{supportEmail}?subject={Uri.EscapeDataString(subject)}";
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record TeamActivationSnapshot(
        Guid TeamId,
        string TeamName,
        string? TeamLevel,
        string? City,
        string? State,
        string? ZipCode,
        string? Email,
        string? PhoneNumber,
        bool IsSearchable,
        int SportCount,
        int OpportunityCount);
}
