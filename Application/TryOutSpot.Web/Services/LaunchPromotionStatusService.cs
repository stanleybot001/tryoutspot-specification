using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;

namespace TryOutSpot.Web.Services;

public interface ILaunchPromotionStatusService
{
    Task<LaunchPromotionStatus> GetLaunchFounderOfferStatusAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken);
}

public sealed class LaunchPromotionStatusService(AppDbContext dbContext) : ILaunchPromotionStatusService
{
    public const int DefaultClaimLimit = 25;
    public const int MaxClaimLimit = 100;

    public async Task<LaunchPromotionStatus> GetLaunchFounderOfferStatusAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, MaxClaimLimit);
        var normalizedOffset = Math.Max(offset, 0);
        var promotionCode = TryOutSpotPromotionCodes.LaunchFirst1000TwoMonths;
        var now = DateTime.UtcNow;

        var claimedCount = await dbContext.PromotionRedemptions
            .AsNoTracking()
            .CountAsync(redemption => redemption.PromotionCode == promotionCode, cancellationToken);
        var remainingCount = Math.Max(TryOutSpotPromotionCodes.LaunchFirst1000MaxRedemptions - claimedCount, 0);

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
            claimedCount,
            remainingCount,
            TryOutSpotPromotionCodes.LaunchFirst1000MaxRedemptions,
            activeGrantCount,
            latestGrantEndsAt,
            normalizedLimit,
            normalizedOffset,
            recentClaims);
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
    int ClaimedCount,
    int RemainingCount,
    int MaxClaims,
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
