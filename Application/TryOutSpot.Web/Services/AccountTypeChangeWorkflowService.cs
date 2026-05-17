using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;

namespace TryOutSpot.Web.Services;

public interface IAccountTypeChangeWorkflowService
{
    Task<AccountTypeWorkflowResult> QueueOrApplyAsync(
        User user,
        IReadOnlyCollection<string> targetPublicRoles,
        CancellationToken cancellationToken);

    Task<bool> ReconcilePendingChangesForUserAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record AccountTypeWorkflowResult(
    bool AppliedImmediately,
    bool Scheduled,
    string Message);

public sealed class AccountTypeChangeWorkflowService(
    AppDbContext dbContext,
    UserManager<User> userManager) : IAccountTypeChangeWorkflowService
{
    public async Task<AccountTypeWorkflowResult> QueueOrApplyAsync(
        User user,
        IReadOnlyCollection<string> targetPublicRoles,
        CancellationToken cancellationToken)
    {
        var currentPublicRoles = (await userManager.GetRolesAsync(user))
            .Where(role => TryOutSpotRoles.PublicOrLegacyRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var requestedRoles = targetPublicRoles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!TryOutSpotRoles.TryValidateSingleRolePerBundle(requestedRoles, out var validationError))
        {
            return new AccountTypeWorkflowResult(
                AppliedImmediately: false,
                Scheduled: false,
                Message: validationError ?? "Invalid role selection.");
        }

        var removingTeamBundle = currentPublicRoles.Any(TryOutSpotRoles.IsTeamBundleRole)
            && !requestedRoles.Any(TryOutSpotRoles.IsTeamBundleRole);
        var removingPlayerBundle = HasAny(currentPublicRoles, [TryOutSpotRoles.Parent, TryOutSpotRoles.Player])
            && !HasAny(requestedRoles, [TryOutSpotRoles.Parent, TryOutSpotRoles.Player]);

        var teamState = removingTeamBundle
            ? await GetBundleBillingStateAsync(user.Id, BundleType.Team, cancellationToken)
            : BundleBillingState.None;
        var playerState = removingPlayerBundle
            ? await GetBundleBillingStateAsync(user.Id, BundleType.PlayerParent, cancellationToken)
            : BundleBillingState.None;

        if (teamState.HasActivePaidWithoutCancellation || playerState.HasActivePaidWithoutCancellation)
        {
            return new AccountTypeWorkflowResult(
                AppliedImmediately: false,
                Scheduled: false,
                Message: "This role removal requires canceling the related paid membership first. Use Account Settings > Membership access, then save account types again.");
        }

        if (teamState.HasActivePaidWithCancellation || playerState.HasActivePaidWithCancellation)
        {
            var applyAfterUtc = MaxNullable(teamState.EarliestPeriodEndUtc, playerState.EarliestPeriodEndUtc);
            await UpsertPendingChangeAsync(user.Id, requestedRoles, applyAfterUtc, cancellationToken);
            return new AccountTypeWorkflowResult(
                AppliedImmediately: false,
                Scheduled: true,
                Message: $"Account-type changes scheduled and will apply after billing period end ({applyAfterUtc:MMM d, yyyy h:mm tt} UTC).");
        }

        await ApplyRolesAndBundleStateAsync(user, requestedRoles, cancellationToken);
        return new AccountTypeWorkflowResult(
            AppliedImmediately: true,
            Scheduled: false,
            Message: "Account types updated.");
    }

    public async Task<bool> ReconcilePendingChangesForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var pending = await dbContext.PendingAccountTypeChanges
            .Where(change => change.UserId == userId && change.Status == "pending")
            .OrderBy(change => change.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (pending is null)
        {
            return false;
        }

        if (pending.ApplyAfterUtc is not null && pending.ApplyAfterUtc > DateTime.UtcNow)
        {
            return false;
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(current => current.Id == userId && current.IsActive, cancellationToken);
        if (user is null)
        {
            pending.Status = "blocked";
            pending.Notes = "User not found or inactive.";
            pending.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        var targetRoles = DeserializeRoles(pending.TargetRolesJson);
        var currentPublicRoles = (await userManager.GetRolesAsync(user))
            .Where(role => TryOutSpotRoles.PublicOrLegacyRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var removingTeamBundle = currentPublicRoles.Any(TryOutSpotRoles.IsTeamBundleRole)
            && !targetRoles.Any(TryOutSpotRoles.IsTeamBundleRole);
        var removingPlayerBundle = HasAny(currentPublicRoles, [TryOutSpotRoles.Parent, TryOutSpotRoles.Player])
            && !HasAny(targetRoles, [TryOutSpotRoles.Parent, TryOutSpotRoles.Player]);

        if (removingTeamBundle)
        {
            var teamState = await GetBundleBillingStateAsync(user.Id, BundleType.Team, cancellationToken);
            if (teamState.HasAnyActivePaid)
            {
                return false;
            }
        }

        if (removingPlayerBundle)
        {
            var playerState = await GetBundleBillingStateAsync(user.Id, BundleType.PlayerParent, cancellationToken);
            if (playerState.HasAnyActivePaid)
            {
                return false;
            }
        }

        await ApplyRolesAndBundleStateAsync(user, targetRoles, cancellationToken);
        pending.Status = "applied";
        pending.ProcessedAt = DateTime.UtcNow;
        pending.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ApplyRolesAndBundleStateAsync(
        User user,
        IReadOnlyCollection<string> targetPublicRoles,
        CancellationToken cancellationToken)
    {
        var currentRoles = await userManager.GetRolesAsync(user);
        var currentPublicRoles = currentRoles
            .Where(role => TryOutSpotRoles.PublicOrLegacyRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var targetRoles = targetPublicRoles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var now = DateTime.UtcNow;

        var removingTeamBundle = currentPublicRoles.Any(TryOutSpotRoles.IsTeamBundleRole)
            && !targetRoles.Any(TryOutSpotRoles.IsTeamBundleRole);
        var removingPlayerBundle = HasAny(currentPublicRoles, [TryOutSpotRoles.Parent, TryOutSpotRoles.Player])
            && !HasAny(targetRoles, [TryOutSpotRoles.Parent, TryOutSpotRoles.Player]);

        if (currentPublicRoles.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, currentPublicRoles);
            if (!removeResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", removeResult.Errors.Select(error => error.Description)));
            }
        }

        var addResult = await userManager.AddToRolesAsync(user, targetRoles);
        if (!addResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", addResult.Errors.Select(error => error.Description)));
        }

        if (removingTeamBundle)
        {
            await SoftDeactivateTeamBundleDataAsync(user.Id, now, cancellationToken);
        }

        if (removingPlayerBundle)
        {
            await SoftDeactivatePlayerBundleDataAsync(user.Id, now, cancellationToken);
        }

        user.UpdatedAt = now;
        await userManager.UpdateAsync(user);
    }

    private async Task UpsertPendingChangeAsync(
        Guid userId,
        IReadOnlyCollection<string> targetRoles,
        DateTime? applyAfterUtc,
        CancellationToken cancellationToken)
    {
        var pending = await dbContext.PendingAccountTypeChanges
            .Where(change => change.UserId == userId && change.Status == "pending")
            .OrderByDescending(change => change.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var serialized = JsonSerializer.Serialize(targetRoles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(role => role));
        var now = DateTime.UtcNow;
        if (pending is null)
        {
            pending = new PendingAccountTypeChange
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TargetRolesJson = serialized,
                RequestedAt = now,
                ApplyAfterUtc = applyAfterUtc,
                Status = "pending",
                UpdatedAt = now
            };
            dbContext.PendingAccountTypeChanges.Add(pending);
        }
        else
        {
            pending.TargetRolesJson = serialized;
            pending.ApplyAfterUtc = applyAfterUtc;
            pending.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<BundleBillingState> GetBundleBillingStateAsync(
        Guid userId,
        BundleType bundleType,
        CancellationToken cancellationToken)
    {
        var subscriptions = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Where(subscription => TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status))
            .ToArrayAsync(cancellationToken);

        var relevant = subscriptions.Where(subscription =>
        {
            var planCode = TryOutSpotBillingCatalog.NormalizePlanCode(subscription.PlanType);
            return bundleType switch
            {
                BundleType.Team => planCode is TryOutSpotPlanCodes.TeamBasic
                    or TryOutSpotPlanCodes.TeamProfessional
                    or TryOutSpotPlanCodes.EnterpriseOrganization,
                BundleType.PlayerParent => planCode is TryOutSpotPlanCodes.PremiumPlayer,
                _ => false
            };
        }).ToArray();

        if (relevant.Length == 0)
        {
            return BundleBillingState.None;
        }

        var activeWithoutCancellation = relevant.Any(subscription => !subscription.CancelAtPeriodEnd);
        var activeWithCancellation = relevant.Any(subscription => subscription.CancelAtPeriodEnd);
        var earliestPeriodEnd = relevant
            .Where(subscription => subscription.CurrentPeriodEnd is not null)
            .Select(subscription => subscription.CurrentPeriodEnd)
            .OrderBy(value => value)
            .FirstOrDefault();

        return new BundleBillingState(
            HasAnyActivePaid: true,
            HasActivePaidWithoutCancellation: activeWithoutCancellation,
            HasActivePaidWithCancellation: activeWithCancellation,
            EarliestPeriodEndUtc: earliestPeriodEnd);
    }

    private async Task SoftDeactivateTeamBundleDataAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var teams = await dbContext.Teams
            .Where(team => team.UserTeamRoles.Any(role => role.UserId == userId))
            .ToArrayAsync(cancellationToken);
        var teamIds = teams.Select(team => team.Id).ToArray();
        foreach (var team in teams)
        {
            team.IsActive = false;
            team.IsSearchable = false;
            team.IsContactInfoVisible = false;
            team.UpdatedAt = now;
        }

        var opportunities = await dbContext.Opportunities
            .Where(opportunity => teamIds.Contains(opportunity.TeamId))
            .Where(opportunity => opportunity.IsActive || opportunity.IsPublished)
            .ToArrayAsync(cancellationToken);
        foreach (var opportunity in opportunities)
        {
            opportunity.IsActive = false;
            opportunity.IsPublished = false;
            opportunity.ExpiresAt ??= now;
            opportunity.UpdatedAt = now;
        }
    }

    private async Task SoftDeactivatePlayerBundleDataAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var playerIds = await dbContext.UserPlayerRelationships
            .AsNoTracking()
            .Where(relationship => relationship.UserId == userId && relationship.CanManage)
            .Select(relationship => relationship.PlayerId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var players = await dbContext.Players
            .Where(player => playerIds.Contains(player.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var player in players)
        {
            player.IsActive = false;
            player.IsSearchable = false;
            player.UpdatedAt = now;
        }

        var listings = await dbContext.PlayerListings
            .Where(listing => listing.UserId == userId)
            .Where(listing => listing.PlayerId.HasValue && playerIds.Contains(listing.PlayerId.Value))
            .Where(listing => listing.IsActive || listing.IsPublished)
            .ToArrayAsync(cancellationToken);
        foreach (var listing in listings)
        {
            listing.IsActive = false;
            listing.IsPublished = false;
            listing.ExpiresAt ??= now;
            listing.UpdatedAt = now;
        }
    }

    private static bool HasAny(IEnumerable<string> roles, IEnumerable<string> candidates)
    {
        return roles.Any(role => candidates.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyCollection<string> DeserializeRoles(string json)
    {
        try
        {
            var roles = JsonSerializer.Deserialize<string[]>(json);
            return roles?.Where(role => !string.IsNullOrWhiteSpace(role)).ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DateTime? MaxNullable(DateTime? left, DateTime? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left > right ? left : right;
    }

    private enum BundleType
    {
        Team,
        PlayerParent
    }

    private sealed record BundleBillingState(
        bool HasAnyActivePaid,
        bool HasActivePaidWithoutCancellation,
        bool HasActivePaidWithCancellation,
        DateTime? EarliestPeriodEndUtc)
    {
        public static BundleBillingState None { get; } = new(false, false, false, null);
    }
}
