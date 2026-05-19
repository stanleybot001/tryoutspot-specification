using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Billing;

namespace TryOutSpot.Web.Services;

public interface ILaunchPromotionStatusService
{
    Task<PromotionCampaign> GetActiveLaunchFounderOfferCampaignAsync(CancellationToken cancellationToken);

    Task<LaunchPromotionStatus> GetLaunchFounderOfferStatusAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<LaunchPromotionAvailability> GetLaunchFounderOfferAvailabilityAsync(
        Guid userId,
        IEnumerable<string> roles,
        CancellationToken cancellationToken);

    Task<LaunchPromotionClaimResult> ClaimLaunchFounderOfferAsync(
        Guid userId,
        IEnumerable<string> roles,
        CancellationToken cancellationToken);

    Task<PromotionCampaign> UpdateLaunchFounderOfferSettingsAsync(
        string? name,
        int maxRedemptions,
        int grantMonths,
        bool isEnabled,
        Guid adminUserId,
        CancellationToken cancellationToken);

    Task<PromotionCampaign> ResetLaunchFounderOfferAsync(
        string? name,
        int maxRedemptions,
        int grantMonths,
        bool isEnabled,
        Guid adminUserId,
        CancellationToken cancellationToken);
}

public sealed class LaunchPromotionStatusService(AppDbContext dbContext) : ILaunchPromotionStatusService
{
    public const int DefaultClaimLimit = 25;
    public const int MaxClaimLimit = 100;
    public const int MinMaxRedemptions = 1;
    public const int MaxMaxRedemptions = 100000;
    public const int MinGrantMonths = 1;
    public const int MaxGrantMonths = 120;

    public async Task<PromotionCampaign> GetActiveLaunchFounderOfferCampaignAsync(CancellationToken cancellationToken)
    {
        var campaign = await dbContext.PromotionCampaigns
            .AsNoTracking()
            .Where(currentCampaign => currentCampaign.IsActive)
            .OrderByDescending(currentCampaign => currentCampaign.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (campaign is not null)
        {
            return campaign;
        }

        return await CreateDefaultLaunchFounderOfferAsync(cancellationToken);
    }

    public async Task<LaunchPromotionStatus> GetLaunchFounderOfferStatusAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, MaxClaimLimit);
        var normalizedOffset = Math.Max(offset, 0);
        var campaign = await GetActiveLaunchFounderOfferCampaignAsync(cancellationToken);
        var promotionCode = campaign.Code;
        var now = DateTime.UtcNow;

        var claimedCount = await dbContext.PromotionRedemptions
            .AsNoTracking()
            .CountAsync(redemption => redemption.PromotionCode == promotionCode, cancellationToken);
        var remainingCount = Math.Max(campaign.MaxRedemptions - claimedCount, 0);

        var activeGrantQuery = dbContext.ComplimentaryPlanGrants
            .AsNoTracking()
            .Where(grant => grant.PromotionCode == promotionCode
                && grant.RevokedAt == null
                && grant.StartsAt <= now
                && (grant.EndsAt == null || grant.EndsAt > now));
        var activeGrantCount = await activeGrantQuery.CountAsync(cancellationToken);
        var latestGrantEndsAt = await activeGrantQuery
            .Select(grant => grant.EndsAt)
            .MaxAsync(cancellationToken);

        var claimProjections = await dbContext.PromotionRedemptions
            .AsNoTracking()
            .Where(redemption => redemption.PromotionCode == promotionCode)
            .OrderByDescending(redemption => redemption.RedeemedAt)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Join(
                dbContext.Users.AsNoTracking(),
                redemption => redemption.UserId,
                user => user.Id,
                (redemption, user) => new PromotionClaimProjection(
                    user.Id,
                    user.FirstName,
                    user.LastName,
                    user.Email ?? string.Empty,
                    redemption.GrantedPlanCodes,
                    redemption.RedeemedAt))
            .ToArrayAsync(cancellationToken);

        var claimUserIds = claimProjections.Select(claim => claim.UserId).ToArray();
        var grantSummaries = claimUserIds.Length == 0
            ? Array.Empty<PromotionGrantSummary>()
            : await dbContext.ComplimentaryPlanGrants
                .AsNoTracking()
                .Where(grant => claimUserIds.Contains(grant.UserId)
                    && grant.PromotionCode == promotionCode
                    && grant.RevokedAt == null
                    && grant.StartsAt <= now
                    && (grant.EndsAt == null || grant.EndsAt > now))
                .GroupBy(grant => grant.UserId)
                .Select(group => new PromotionGrantSummary(
                    group.Key,
                    group.Count(),
                    group.Max(grant => grant.EndsAt)))
                .ToArrayAsync(cancellationToken);
        var grantSummaryLookup = grantSummaries.ToDictionary(summary => summary.UserId);

        var recentClaims = claimProjections
            .Select(claim =>
            {
                grantSummaryLookup.TryGetValue(claim.UserId, out var grantSummary);
                return new LaunchPromotionClaimStatus(
                    claim.UserId,
                    GetDisplayName(claim.FirstName, claim.LastName, claim.Email),
                    claim.Email,
                    SplitPlanCodes(claim.GrantedPlanCodes),
                    grantSummary?.ActiveGrantCount ?? 0,
                    grantSummary?.LatestGrantEndsAtUtc,
                    claim.RedeemedAtUtc);
            })
            .ToArray();

        return new LaunchPromotionStatus(
            promotionCode,
            campaign.Name,
            campaign.IsEnabled,
            claimedCount,
            remainingCount,
            campaign.MaxRedemptions,
            campaign.GrantMonths,
            activeGrantCount,
            latestGrantEndsAt,
            normalizedLimit,
            normalizedOffset,
            recentClaims);
    }

    public async Task<LaunchPromotionAvailability> GetLaunchFounderOfferAvailabilityAsync(
        Guid userId,
        IEnumerable<string> roles,
        CancellationToken cancellationToken)
    {
        var campaign = await GetActiveLaunchFounderOfferCampaignAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var claimedCount = await dbContext.PromotionRedemptions
            .AsNoTracking()
            .CountAsync(redemption => redemption.PromotionCode == campaign.Code, cancellationToken);
        var remainingCount = Math.Max(campaign.MaxRedemptions - claimedCount, 0);
        var planCodes = GetLaunchGrantPlanCodes(roles);

        var previousGrants = await GetLaunchGrantResponsesAsync(
            userId,
            campaign.Code,
            now,
            cancellationToken);
        var hasAlreadyClaimed = await dbContext.PromotionRedemptions
            .AsNoTracking()
            .AnyAsync(redemption =>
                    redemption.PromotionCode == campaign.Code
                    && redemption.UserId == userId,
                cancellationToken);
        var activeAccessPlanCodes = await GetActiveAccessPlanCodesAsync(
            userId,
            now,
            cancellationToken);
        var hasActiveAccessForEligiblePlans = planCodes.Length > 0
            && planCodes.All(activeAccessPlanCodes.Contains);
        var existingClaimEndsAt = previousGrants
            .Where(grant => grant.HasActiveEntitlement)
            .Select(grant => grant.EndsAt)
            .DefaultIfEmpty()
            .Max();

        return new LaunchPromotionAvailability(
            campaign.Code,
            campaign.Name,
            campaign.IsEnabled,
            planCodes.Length > 0,
            hasAlreadyClaimed,
            remainingCount == 0,
            hasActiveAccessForEligiblePlans,
            claimedCount,
            remainingCount,
            campaign.MaxRedemptions,
            campaign.GrantMonths,
            existingClaimEndsAt,
            planCodes);
    }

    public async Task<LaunchPromotionClaimResult> ClaimLaunchFounderOfferAsync(
        Guid userId,
        IEnumerable<string> roles,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(currentUser => currentUser.Id == userId && currentUser.IsActive, cancellationToken);
        if (user is null)
        {
            return LaunchPromotionClaimResult.UserNotFound();
        }

        var planCodes = GetLaunchGrantPlanCodes(roles);
        if (planCodes.Length == 0)
        {
            return LaunchPromotionClaimResult.NoEligibleAccountType();
        }

        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var campaign = await GetTrackedActiveLaunchFounderOfferCampaignAsync(cancellationToken);
        var existingRedemption = await dbContext.PromotionRedemptions
            .AsNoTracking()
            .SingleOrDefaultAsync(redemption =>
                    redemption.PromotionCode == campaign.Code
                    && redemption.UserId == userId,
                cancellationToken);
        if (existingRedemption is not null)
        {
            var previousGrants = await GetLaunchGrantResponsesAsync(userId, campaign.Code, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return LaunchPromotionClaimResult.AlreadyClaimed(
                campaign.Code,
                previousGrants.Where(grant => grant.HasActiveEntitlement).Select(grant => grant.EndsAt).DefaultIfEmpty().Max(),
                previousGrants);
        }

        if (!campaign.IsEnabled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return LaunchPromotionClaimResult.PromotionDisabled(campaign.Code);
        }

        var redemptionCount = await dbContext.PromotionRedemptions
            .CountAsync(redemption => redemption.PromotionCode == campaign.Code, cancellationToken);
        if (redemptionCount >= campaign.MaxRedemptions)
        {
            await transaction.RollbackAsync(cancellationToken);
            return LaunchPromotionClaimResult.PromotionExhausted(campaign.Code);
        }

        var activeAccessPlanCodes = await GetActiveAccessPlanCodesAsync(userId, now, cancellationToken);
        var planCodesToGrant = planCodes
            .Where(planCode => !activeAccessPlanCodes.Contains(planCode))
            .ToArray();

        if (planCodesToGrant.Length == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return LaunchPromotionClaimResult.AlreadyHasAccess(campaign.Code);
        }

        var grantEndsAt = now.AddMonths(campaign.GrantMonths);
        var grants = planCodesToGrant
            .Select(planCode => new ComplimentaryPlanGrant
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanType = planCode,
                ScopeType = TryOutSpotSubscriptionScopeTypes.Account,
                StartsAt = now,
                EndsAt = grantEndsAt,
                Source = TryOutSpotPromotionCodes.LaunchPromotionGrantSource,
                PromotionCode = campaign.Code,
                Reason = $"Launch promotion: {campaign.Name} grants {campaign.GrantMonths} free month(s).",
                GrantedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            })
            .ToArray();

        dbContext.ComplimentaryPlanGrants.AddRange(grants);
        dbContext.PromotionRedemptions.Add(new PromotionRedemption
        {
            Id = Guid.NewGuid(),
            PromotionCode = campaign.Code,
            UserId = userId,
            GrantedPlanCodes = string.Join(",", planCodesToGrant),
            RedeemedAt = now
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicatePromotionRedemption(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return LaunchPromotionClaimResult.Conflict(campaign.Code);
        }

        return LaunchPromotionClaimResult.Claimed(
            campaign.Code,
            grantEndsAt,
            grants.Select(grant => ComplimentaryPlanGrantMapper.ToResponse(grant, now)).ToArray());
    }

    public async Task<PromotionCampaign> UpdateLaunchFounderOfferSettingsAsync(
        string? name,
        int maxRedemptions,
        int grantMonths,
        bool isEnabled,
        Guid adminUserId,
        CancellationToken cancellationToken)
    {
        var campaign = await GetTrackedActiveLaunchFounderOfferCampaignAsync(cancellationToken);
        var now = DateTime.UtcNow;

        campaign.Name = NormalizeCampaignName(name);
        campaign.MaxRedemptions = NormalizeMaxRedemptions(maxRedemptions);
        campaign.GrantMonths = NormalizeGrantMonths(grantMonths);
        campaign.IsEnabled = isEnabled;
        campaign.UpdatedByUserId = adminUserId;
        campaign.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return campaign;
    }

    public async Task<PromotionCampaign> ResetLaunchFounderOfferAsync(
        string? name,
        int maxRedemptions,
        int grantMonths,
        bool isEnabled,
        Guid adminUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var activeCampaigns = await dbContext.PromotionCampaigns
            .Where(campaign => campaign.IsActive)
            .ToArrayAsync(cancellationToken);
        foreach (var activeCampaign in activeCampaigns)
        {
            activeCampaign.IsActive = false;
            activeCampaign.UpdatedByUserId = adminUserId;
            activeCampaign.UpdatedAt = now;
        }

        var campaign = new PromotionCampaign
        {
            Id = Guid.NewGuid(),
            Code = CreateResetCampaignCode(now),
            Name = NormalizeCampaignName(name),
            MaxRedemptions = NormalizeMaxRedemptions(maxRedemptions),
            GrantMonths = NormalizeGrantMonths(grantMonths),
            IsActive = true,
            IsEnabled = isEnabled,
            CreatedByUserId = adminUserId,
            UpdatedByUserId = adminUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.PromotionCampaigns.Add(campaign);
        await dbContext.SaveChangesAsync(cancellationToken);

        return campaign;
    }

    private async Task<PromotionCampaign> GetTrackedActiveLaunchFounderOfferCampaignAsync(CancellationToken cancellationToken)
    {
        var campaign = await dbContext.PromotionCampaigns
            .Where(currentCampaign => currentCampaign.IsActive)
            .OrderByDescending(currentCampaign => currentCampaign.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (campaign is not null)
        {
            return campaign;
        }

        return await CreateDefaultLaunchFounderOfferAsync(cancellationToken);
    }

    private async Task<PromotionCampaign> CreateDefaultLaunchFounderOfferAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var existingCampaign = await dbContext.PromotionCampaigns
            .FirstOrDefaultAsync(campaign => campaign.Id == TryOutSpotPromotionCodes.LaunchFounderOfferCampaignId
                || campaign.Code == TryOutSpotPromotionCodes.LaunchFirst1000TwoMonths,
                cancellationToken);
        if (existingCampaign is not null)
        {
            existingCampaign.IsActive = true;
            existingCampaign.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existingCampaign;
        }

        var campaign = new PromotionCampaign
        {
            Id = TryOutSpotPromotionCodes.LaunchFounderOfferCampaignId,
            Code = TryOutSpotPromotionCodes.LaunchFirst1000TwoMonths,
            Name = TryOutSpotPromotionCodes.LaunchFounderOfferName,
            MaxRedemptions = TryOutSpotPromotionCodes.LaunchFirst1000MaxRedemptions,
            GrantMonths = TryOutSpotPromotionCodes.LaunchFirst1000GrantMonths,
            IsActive = true,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.PromotionCampaigns.Add(campaign);
        await dbContext.SaveChangesAsync(cancellationToken);
        return campaign;
    }

    private static string NormalizeCampaignName(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? TryOutSpotPromotionCodes.LaunchFounderOfferName
            : name.Trim()[..Math.Min(name.Trim().Length, 200)];
    }

    private static int NormalizeMaxRedemptions(int maxRedemptions)
    {
        return Math.Clamp(maxRedemptions, MinMaxRedemptions, MaxMaxRedemptions);
    }

    private static int NormalizeGrantMonths(int grantMonths)
    {
        return Math.Clamp(grantMonths, MinGrantMonths, MaxGrantMonths);
    }

    private static string CreateResetCampaignCode(DateTime now)
    {
        return $"launch_founder_offer_{now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
    }

    private static string[] SplitPlanCodes(string planCodes)
    {
        return planCodes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(planCode => TryOutSpotBillingCatalog.NormalizePlanCode(planCode) ?? planCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<ComplimentaryPlanGrantResponse[]> GetLaunchGrantResponsesAsync(
        Guid userId,
        string promotionCode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var grants = await dbContext.ComplimentaryPlanGrants
            .AsNoTracking()
            .Where(grant => grant.UserId == userId
                && grant.PromotionCode == promotionCode)
            .OrderByDescending(grant => grant.CreatedAt)
            .ToArrayAsync(cancellationToken);
        return grants
            .Select(grant => ComplimentaryPlanGrantMapper.ToResponse(grant, now))
            .ToArray();
    }

    private async Task<HashSet<string>> GetActiveAccessPlanCodesAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var subscriptions = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId
                && subscription.ScopeType == TryOutSpotSubscriptionScopeTypes.Account)
            .Select(subscription => new { subscription.PlanType, subscription.Status })
            .ToArrayAsync(cancellationToken);
        var activeSubscriptionPlanCodes = subscriptions
            .Where(subscription => TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status))
            .Select(subscription => TryOutSpotBillingCatalog.NormalizePlanCode(subscription.PlanType))
            .Where(planCode => planCode is not null)
            .Cast<string>();

        var activeGrantPlanCodes = await dbContext.ComplimentaryPlanGrants
            .AsNoTracking()
            .Where(grant => grant.UserId == userId
                && grant.RevokedAt == null
                && grant.ScopeType == TryOutSpotSubscriptionScopeTypes.Account
                && grant.StartsAt <= now
                && (grant.EndsAt == null || grant.EndsAt > now))
            .Select(grant => grant.PlanType)
            .ToArrayAsync(cancellationToken);
        var activeGrantPlanCodeSet = activeGrantPlanCodes
            .Select(TryOutSpotBillingCatalog.NormalizePlanCode)
            .Where(planCode => planCode is not null)
            .Cast<string>();

        return activeSubscriptionPlanCodes
            .Concat(activeGrantPlanCodeSet)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string[] GetLaunchGrantPlanCodes(IEnumerable<string> roles)
    {
        var planCodes = new List<string>();
        var normalizedRoles = TryOutSpotRoles.CanonicalizeRoleSet(roles);
        if (normalizedRoles.Any(TryOutSpotRoles.IsPlayerParentRole))
        {
            planCodes.Add(TryOutSpotPlanCodes.PremiumPlayer);
        }

        if (normalizedRoles.Any(TryOutSpotRoles.IsTeamBundleRole))
        {
            planCodes.Add(TryOutSpotPlanCodes.TeamBasic);
        }

        return planCodes.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsDuplicatePromotionRedemption(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && string.Equals(postgresException.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal);
    }

    private static string GetDisplayName(string firstName, string lastName, string email)
    {
        var displayName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(displayName) ? email : displayName;
    }

    private sealed record PromotionClaimProjection(
        Guid UserId,
        string FirstName,
        string LastName,
        string Email,
        string GrantedPlanCodes,
        DateTime RedeemedAtUtc);

    private sealed record PromotionGrantSummary(
        Guid UserId,
        int ActiveGrantCount,
        DateTime? LatestGrantEndsAtUtc);
}

public sealed record LaunchPromotionStatus(
    string PromotionCode,
    string PromotionName,
    bool IsEnabled,
    int ClaimedCount,
    int RemainingCount,
    int MaxClaims,
    int GrantMonths,
    int ActiveGrantCount,
    DateTime? LatestGrantEndsAtUtc,
    int Limit,
    int Offset,
    IReadOnlyCollection<LaunchPromotionClaimStatus> RecentClaims);

public sealed record LaunchPromotionClaimStatus(
    Guid UserId,
    string UserDisplayName,
    string UserEmail,
    IReadOnlyCollection<string> GrantedPlanCodes,
    int ActiveGrantCount,
    DateTime? LatestGrantEndsAtUtc,
    DateTime RedeemedAtUtc);

public sealed record LaunchPromotionAvailability(
    string PromotionCode,
    string PromotionName,
    bool IsEnabled,
    bool IsEligibleForCurrentAccountType,
    bool HasAlreadyClaimed,
    bool IsExhausted,
    bool HasActiveAccessForEligiblePlans,
    int ClaimedCount,
    int RemainingCount,
    int MaxClaims,
    int GrantMonths,
    DateTime? ExistingClaimEndsAtUtc,
    IReadOnlyCollection<string> EligiblePlanCodes)
{
    public bool CanClaim => IsEnabled
        && IsEligibleForCurrentAccountType
        && !HasAlreadyClaimed
        && !IsExhausted
        && !HasActiveAccessForEligiblePlans;
}

public sealed record LaunchPromotionClaimResult(
    LaunchPromotionClaimResultStatus Status,
    string PromotionCode,
    DateTime? EndsAt,
    IReadOnlyCollection<ComplimentaryPlanGrantResponse> Grants)
{
    public static LaunchPromotionClaimResult Claimed(
        string promotionCode,
        DateTime endsAt,
        IReadOnlyCollection<ComplimentaryPlanGrantResponse> grants)
    {
        return new LaunchPromotionClaimResult(
            LaunchPromotionClaimResultStatus.Claimed,
            promotionCode,
            endsAt,
            grants);
    }

    public static LaunchPromotionClaimResult AlreadyClaimed(
        string promotionCode,
        DateTime? endsAt,
        IReadOnlyCollection<ComplimentaryPlanGrantResponse> grants)
    {
        return new LaunchPromotionClaimResult(
            LaunchPromotionClaimResultStatus.AlreadyClaimed,
            promotionCode,
            endsAt,
            grants);
    }

    public static LaunchPromotionClaimResult NoEligibleAccountType()
    {
        return new LaunchPromotionClaimResult(
            LaunchPromotionClaimResultStatus.NoEligibleAccountType,
            string.Empty,
            null,
            []);
    }

    public static LaunchPromotionClaimResult PromotionDisabled(string promotionCode)
    {
        return new LaunchPromotionClaimResult(
            LaunchPromotionClaimResultStatus.PromotionDisabled,
            promotionCode,
            null,
            []);
    }

    public static LaunchPromotionClaimResult PromotionExhausted(string promotionCode)
    {
        return new LaunchPromotionClaimResult(
            LaunchPromotionClaimResultStatus.PromotionExhausted,
            promotionCode,
            null,
            []);
    }

    public static LaunchPromotionClaimResult AlreadyHasAccess(string promotionCode)
    {
        return new LaunchPromotionClaimResult(
            LaunchPromotionClaimResultStatus.AlreadyHasAccess,
            promotionCode,
            null,
            []);
    }

    public static LaunchPromotionClaimResult Conflict(string promotionCode)
    {
        return new LaunchPromotionClaimResult(
            LaunchPromotionClaimResultStatus.Conflict,
            promotionCode,
            null,
            []);
    }

    public static LaunchPromotionClaimResult UserNotFound()
    {
        return new LaunchPromotionClaimResult(
            LaunchPromotionClaimResultStatus.UserNotFound,
            string.Empty,
            null,
            []);
    }
}

public enum LaunchPromotionClaimResultStatus
{
    Claimed,
    AlreadyClaimed,
    NoEligibleAccountType,
    PromotionDisabled,
    PromotionExhausted,
    AlreadyHasAccess,
    Conflict,
    UserNotFound
}
