using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

[ApiController]
[Authorize(Policy = TryOutSpotAuthorizationPolicies.ConfirmedEmail)]
[Tags("Billing")]
[Produces("application/json")]
[Route("api/billing/promotions")]
public sealed class BillingPromotionsApiController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    ILaunchPromotionStatusService launchPromotionStatusService) : ControllerBase
{
    [HttpPost("launch-founder-offer/claim")]
    [ProducesResponseType<PromotionClaimResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PromotionClaimResponse>> ClaimLaunchFounderOffer(
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserWithSubscriptionsAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var roles = TryOutSpotRoles.CanonicalizeRoleSet(await userManager.GetRolesAsync(user));
        var planCodes = GetLaunchGrantPlanCodes(roles);
        if (planCodes.Length == 0)
        {
            ModelState.AddModelError("accountTypes", "This promotion requires a parent, player, or team representative account type.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var campaign = await launchPromotionStatusService.GetActiveLaunchFounderOfferCampaignAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existingRedemption = await dbContext.PromotionRedemptions
            .AsNoTracking()
            .SingleOrDefaultAsync(redemption =>
                    redemption.PromotionCode == campaign.Code
                    && redemption.UserId == user.Id,
                cancellationToken);
        if (existingRedemption is not null)
        {
            var previousGrants = await GetLaunchGrantResponsesAsync(user.Id, campaign.Code, now, cancellationToken);
            return Ok(new PromotionClaimResponse(
                campaign.Code,
                false,
                previousGrants.Where(grant => grant.HasActiveEntitlement).Select(grant => grant.EndsAt).DefaultIfEmpty().Max(),
                previousGrants));
        }

        var redemptionCount = await dbContext.PromotionRedemptions
            .CountAsync(redemption => redemption.PromotionCode == campaign.Code, cancellationToken);
        if (redemptionCount >= campaign.MaxRedemptions)
        {
            return Conflict(new PromotionClaimResponse(
                campaign.Code,
                false,
                null,
                []));
        }

        var activeSubscriptionPlanCodes = user.Subscriptions
            .Where(subscription => TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status)
                && string.Equals(subscription.ScopeType, TryOutSpotSubscriptionScopeTypes.Account, StringComparison.Ordinal))
            .Select(subscription => TryOutSpotBillingCatalog.NormalizePlanCode(subscription.PlanType))
            .Where(planCode => planCode is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var activeGrantPlanCodes = await dbContext.ComplimentaryPlanGrants
            .AsNoTracking()
            .Where(grant => grant.UserId == user.Id
                && grant.RevokedAt == null
                && grant.ScopeType == TryOutSpotSubscriptionScopeTypes.Account
                && grant.StartsAt <= now
                && (grant.EndsAt == null || grant.EndsAt > now))
            .Select(grant => grant.PlanType)
            .ToArrayAsync(cancellationToken);
        var grantPlanCodeSet = activeGrantPlanCodes
            .Select(TryOutSpotBillingCatalog.NormalizePlanCode)
            .Where(planCode => planCode is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var planCodesToGrant = planCodes
            .Where(planCode => !activeSubscriptionPlanCodes.Contains(planCode)
                && !grantPlanCodeSet.Contains(planCode))
            .ToArray();

        if (planCodesToGrant.Length == 0)
        {
            ModelState.AddModelError("promotion", "This account already has active access for the launch promotion plans.");
            return ValidationProblem(ModelState);
        }

        var grantEndsAt = now.AddMonths(campaign.GrantMonths);
        var grants = planCodesToGrant
            .Select(planCode => new ComplimentaryPlanGrant
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PlanType = planCode,
                ScopeType = TryOutSpotSubscriptionScopeTypes.Account,
                StartsAt = now,
                EndsAt = grantEndsAt,
                Source = TryOutSpotPromotionCodes.LaunchPromotionGrantSource,
                PromotionCode = campaign.Code,
                Reason = $"Launch promotion: {campaign.Name} grants {campaign.GrantMonths} free month(s).",
                GrantedByUserId = user.Id,
                CreatedAt = now,
                UpdatedAt = now
            })
            .ToArray();

        dbContext.ComplimentaryPlanGrants.AddRange(grants);
        dbContext.PromotionRedemptions.Add(new PromotionRedemption
        {
            Id = Guid.NewGuid(),
            PromotionCode = campaign.Code,
            UserId = user.Id,
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
            return Conflict(new PromotionClaimResponse(
                campaign.Code,
                false,
                null,
                []));
        }

        return Ok(new PromotionClaimResponse(
            campaign.Code,
            true,
            grantEndsAt,
            grants.Select(grant => ComplimentaryPlanGrantMapper.ToResponse(grant, now)).ToArray()));
    }

    private async Task<User?> GetCurrentUserWithSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return await dbContext.Users
            .Include(user => user.Subscriptions)
            .SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, cancellationToken);
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
}
