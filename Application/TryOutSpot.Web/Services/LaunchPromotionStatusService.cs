using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public interface ILaunchPromotionStatusService
{
    Task<PromotionCampaign> GetActiveLaunchFounderOfferCampaignAsync(CancellationToken cancellationToken);

    Task<LaunchPromotionStatus> GetLaunchFounderOfferStatusAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<PromotionCampaign> UpdateLaunchFounderOfferSettingsAsync(
        string? name,
        int maxRedemptions,
        int grantMonths,
        Guid adminUserId,
        CancellationToken cancellationToken);

    Task<PromotionCampaign> ResetLaunchFounderOfferAsync(
        string? name,
        int maxRedemptions,
        int grantMonths,
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

    public async Task<PromotionCampaign> UpdateLaunchFounderOfferSettingsAsync(
        string? name,
        int maxRedemptions,
        int grantMonths,
        Guid adminUserId,
        CancellationToken cancellationToken)
    {
        var campaign = await GetTrackedActiveLaunchFounderOfferCampaignAsync(cancellationToken);
        var now = DateTime.UtcNow;

        campaign.Name = NormalizeCampaignName(name);
        campaign.MaxRedemptions = NormalizeMaxRedemptions(maxRedemptions);
        campaign.GrantMonths = NormalizeGrantMonths(grantMonths);
        campaign.UpdatedByUserId = adminUserId;
        campaign.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return campaign;
    }

    public async Task<PromotionCampaign> ResetLaunchFounderOfferAsync(
        string? name,
        int maxRedemptions,
        int grantMonths,
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
