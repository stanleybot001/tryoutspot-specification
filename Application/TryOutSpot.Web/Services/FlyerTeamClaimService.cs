using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Dashboard;

namespace TryOutSpot.Web.Services;

public sealed class FlyerTeamClaimService(
    AppDbContext dbContext,
    UserManager<User> userManager) : IFlyerTeamClaimService
{
    private const string DismissedEventType = "dismissed";
    private const int CandidateTeamScanLimit = 500;
    private const int ClaimPromptLimit = 10;

    public async Task<IReadOnlyCollection<FlyerTeamClaimCandidate>> GetClaimableTeamsAsync(
        User user,
        CancellationToken cancellationToken)
    {
        return await LoadClaimableTeamsAsync(user, null, ClaimPromptLimit, cancellationToken);
    }

    public async Task<FlyerTeamClaimResult> ClaimAsync(
        User user,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var candidate = (await LoadClaimableTeamsAsync(user, teamId, null, cancellationToken)).SingleOrDefault();
        if (candidate is null)
        {
            return new FlyerTeamClaimResult(
                false,
                "We could not match that team to a verified email or phone number on your account.");
        }

        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var team = await dbContext.Teams
            .Include(currentTeam => currentTeam.UserTeamRoles)
            .SingleOrDefaultAsync(currentTeam => currentTeam.Id == teamId, cancellationToken);
        if (team is null)
        {
            return new FlyerTeamClaimResult(false, "That team could not be found.");
        }

        var activeRole = team.UserTeamRoles.FirstOrDefault(teamRole => teamRole.IsActive);
        if (activeRole is not null && activeRole.UserId != user.Id)
        {
            return new FlyerTeamClaimResult(false, "That team has already been claimed.");
        }

        var existingUserTeamRole = team.UserTeamRoles.FirstOrDefault(teamRole => teamRole.UserId == user.Id);
        if (existingUserTeamRole is null)
        {
            dbContext.UserTeamRoles.Add(new UserTeamRole
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TeamId = team.Id,
                Role = TryOutSpotRoles.TeamRepresentative,
                StartDate = now,
                IsActive = true,
                CreatedAt = now
            });
        }
        else
        {
            existingUserTeamRole.Role = TryOutSpotRoles.TeamRepresentative;
            existingUserTeamRole.StartDate = now;
            existingUserTeamRole.EndDate = null;
            existingUserTeamRole.IsActive = true;
        }

        team.IsActive = true;
        team.IsSearchable = true;
        team.IsContactInfoVisible = true;
        team.UpdatedAt = now;

        var addedTeamRole = false;
        if (!await userManager.IsInRoleAsync(user, TryOutSpotRoles.TeamRepresentative))
        {
            var addRoleResult = await userManager.AddToRoleAsync(user, TryOutSpotRoles.TeamRepresentative);
            if (!addRoleResult.Succeeded)
            {
                return new FlyerTeamClaimResult(
                    false,
                    string.Join(" ", addRoleResult.Errors.Select(error => error.Description)));
            }

            addedTeamRole = true;
        }

        user.UpdatedAt = now;
        await userManager.UpdateAsync(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new FlyerTeamClaimResult(
            true,
            $"You can now manage {candidate.TeamName} and its listings from your team dashboard.",
            addedTeamRole);
    }

    public async Task<FlyerTeamClaimResult> DismissAsync(
        User user,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var candidate = (await LoadClaimableTeamsAsync(user, teamId, null, cancellationToken)).SingleOrDefault();
        if (candidate is null)
        {
            return new FlyerTeamClaimResult(true, "That team claim is no longer available.");
        }

        dbContext.ActivationAssistanceEvents.Add(new ActivationAssistanceEvent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TeamId = teamId,
            PromptKey = ActivationAssistancePromptKeys.FlyerTeamClaim,
            EventType = DismissedEventType,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new FlyerTeamClaimResult(true, $"We will stop showing {candidate.TeamName} as a team to claim.");
    }

    private async Task<IReadOnlyCollection<FlyerTeamClaimCandidate>> LoadClaimableTeamsAsync(
        User user,
        Guid? teamId,
        int? limit,
        CancellationToken cancellationToken)
    {
        var verifiedEmail = user.EmailConfirmed ? NormalizeEmail(user.Email) : null;
        var verifiedPhone = user.PhoneNumberConfirmed ? NormalizePhoneDigits(user.PhoneNumber) : null;
        if (verifiedEmail is null && verifiedPhone is null)
        {
            return [];
        }

        var candidateTeamQuery = dbContext.FlyerImports
            .AsNoTracking()
            .Where(flyerImport => flyerImport.TeamId.HasValue)
            .Where(flyerImport => flyerImport.Team != null)
            .Where(flyerImport => !flyerImport.Team!.UserTeamRoles.Any(teamRole => teamRole.IsActive))
            .Where(flyerImport => !dbContext.ActivationAssistanceEvents.Any(assistanceEvent =>
                assistanceEvent.UserId == user.Id
                && assistanceEvent.TeamId == flyerImport.TeamId
                && assistanceEvent.PromptKey == ActivationAssistancePromptKeys.FlyerTeamClaim
                && assistanceEvent.EventType == DismissedEventType));
        if (teamId.HasValue)
        {
            candidateTeamQuery = candidateTeamQuery.Where(flyerImport => flyerImport.TeamId == teamId.Value);
        }

        var candidateTeamIds = await candidateTeamQuery
            .GroupBy(flyerImport => flyerImport.TeamId!.Value)
            .Select(group => new
            {
                TeamId = group.Key,
                LatestFlyerAt = group.Max(flyerImport => flyerImport.CreatedAt)
            })
            .OrderByDescending(candidate => candidate.LatestFlyerAt)
            .Take(teamId.HasValue ? 1 : CandidateTeamScanLimit)
            .Select(candidate => candidate.TeamId)
            .ToArrayAsync(cancellationToken);
        if (candidateTeamIds.Length == 0)
        {
            return [];
        }

        var dismissedTeamIds = await dbContext.ActivationAssistanceEvents
            .AsNoTracking()
            .Where(assistanceEvent => assistanceEvent.UserId == user.Id)
            .Where(assistanceEvent => assistanceEvent.PromptKey == ActivationAssistancePromptKeys.FlyerTeamClaim)
            .Where(assistanceEvent => assistanceEvent.EventType == DismissedEventType)
            .Where(assistanceEvent => assistanceEvent.TeamId.HasValue)
            .Select(assistanceEvent => assistanceEvent.TeamId!.Value)
            .ToArrayAsync(cancellationToken);
        var dismissedTeamIdSet = dismissedTeamIds.ToHashSet();

        var teams = await dbContext.Teams
            .AsNoTracking()
            .Include(team => team.TeamSports)
                .ThenInclude(teamSport => teamSport.Sport)
            .Where(team => candidateTeamIds.Contains(team.Id))
            .Where(team => !team.UserTeamRoles.Any(teamRole => teamRole.IsActive))
            .ToArrayAsync(cancellationToken);
        if (teams.Length == 0)
        {
            return [];
        }

        var teamIds = teams.Select(team => team.Id).ToArray();
        var flyerImports = await dbContext.FlyerImports
            .AsNoTracking()
            .Where(flyerImport => flyerImport.TeamId.HasValue && teamIds.Contains(flyerImport.TeamId.Value))
            .OrderByDescending(flyerImport => flyerImport.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var flyerImportsByTeamId = flyerImports
            .GroupBy(flyerImport => flyerImport.TeamId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var activeListingCountsByTeamId = await dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => teamIds.Contains(opportunity.TeamId) && opportunity.IsActive)
            .GroupBy(opportunity => opportunity.TeamId)
            .Select(group => new
            {
                TeamId = group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(group => group.TeamId, group => group.Count, cancellationToken);

        var candidates = new List<FlyerTeamClaimCandidate>();
        foreach (var team in teams)
        {
            if (dismissedTeamIdSet.Contains(team.Id))
            {
                continue;
            }

            if (!flyerImportsByTeamId.TryGetValue(team.Id, out var teamFlyers) || teamFlyers.Length == 0)
            {
                continue;
            }

            var matchedBy = ResolveVerifiedContactMatch(team, teamFlyers, verifiedEmail, verifiedPhone);
            if (matchedBy is null)
            {
                continue;
            }

            var latestFlyer = teamFlyers[0];
            candidates.Add(new FlyerTeamClaimCandidate(
                team.Id,
                team.Name,
                NormalizeOptional(team.TeamLevel)
                    ?? team.TeamSports.FirstOrDefault(teamSport => teamSport.IsActive)?.AgeGroup
                    ?? latestFlyer.AgeGroup,
                team.TeamSports
                    .Where(teamSport => teamSport.IsActive)
                    .OrderBy(teamSport => teamSport.Sport.Name)
                    .Select(teamSport => teamSport.Sport.Name)
                    .FirstOrDefault()
                    ?? latestFlyer.SportName,
                NormalizeOptional(team.City) ?? latestFlyer.City,
                NormalizeOptional(team.State) ?? latestFlyer.State,
                NormalizeOptional(team.ZipCode) ?? latestFlyer.ZipCode,
                matchedBy,
                activeListingCountsByTeamId.GetValueOrDefault(team.Id),
                latestFlyer.CreatedAt));
        }

        var orderedCandidates = candidates
            .OrderByDescending(candidate => candidate.LatestFlyerAt)
            .ThenBy(candidate => candidate.TeamName, StringComparer.OrdinalIgnoreCase);

        return limit.HasValue
            ? orderedCandidates.Take(limit.Value).ToArray()
            : orderedCandidates.ToArray();
    }

    private static string? ResolveVerifiedContactMatch(
        Team team,
        IReadOnlyCollection<FlyerImport> flyerImports,
        string? verifiedEmail,
        string? verifiedPhone)
    {
        var emailMatches = verifiedEmail is not null
            && (string.Equals(NormalizeEmail(team.Email), verifiedEmail, StringComparison.Ordinal)
                || flyerImports.Any(flyerImport =>
                    string.Equals(NormalizeEmail(flyerImport.ContactEmail), verifiedEmail, StringComparison.Ordinal)));
        var phoneMatches = verifiedPhone is not null
            && (string.Equals(NormalizePhoneDigits(team.PhoneNumber), verifiedPhone, StringComparison.Ordinal)
                || flyerImports.Any(flyerImport =>
                    string.Equals(NormalizePhoneDigits(flyerImport.ContactPhone), verifiedPhone, StringComparison.Ordinal)));

        return (emailMatches, phoneMatches) switch
        {
            (true, true) => "email and phone",
            (true, false) => "email",
            (false, true) => "phone",
            _ => null
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeEmail(string? value)
    {
        return NormalizeOptional(value)?.ToLowerInvariant();
    }

    private static string? NormalizePhoneDigits(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        var digits = string.Concat(normalized.Where(char.IsDigit));
        if (digits.Length == 0)
        {
            return null;
        }

        return digits.Length > 10 && digits.StartsWith('1')
            ? digits[^10..]
            : digits;
    }
}
