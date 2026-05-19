using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Billing;

namespace TryOutSpot.Web.Controllers;

[ApiController]
[Authorize(Roles = TryOutSpotRoles.PlatformAdmin)]
[Tags("Billing")]
[Produces("application/json")]
[Route("api/admin/billing/grants")]
public sealed class AdminBillingGrantsApiController(
    AppDbContext dbContext,
    UserManager<User> userManager) : ControllerBase
{
    private const int DefaultGrantPageSize = 50;
    private const int MaxGrantPageSize = 100;

    [HttpGet]
    [ProducesResponseType<ComplimentaryPlanGrantResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyCollection<ComplimentaryPlanGrantResponse>>> ListGrants(
        Guid? userId,
        bool activeOnly = false,
        int limit = DefaultGrantPageSize,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var normalizedLimit = Math.Clamp(limit, 1, MaxGrantPageSize);
        var normalizedOffset = Math.Max(offset, 0);

        var query = dbContext.ComplimentaryPlanGrants.AsNoTracking();
        if (userId is not null)
        {
            query = query.Where(grant => grant.UserId == userId.Value);
        }

        if (activeOnly)
        {
            query = query.Where(grant => grant.RevokedAt == null
                && grant.StartsAt <= now
                && (grant.EndsAt == null || grant.EndsAt > now));
        }

        var grantEntities = await query
            .OrderByDescending(grant => grant.CreatedAt)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .ToArrayAsync(cancellationToken);
        var grants = grantEntities
            .Select(grant => ComplimentaryPlanGrantMapper.ToResponse(grant, now))
            .ToArray();

        return Ok(grants);
    }

    [HttpPost]
    [ProducesResponseType<ComplimentaryPlanGrantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ComplimentaryPlanGrantResponse>> CreateGrant(
        CreateComplimentaryPlanGrantRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var now = DateTime.UtcNow;
        var targetUser = await dbContext.Users
            .SingleOrDefaultAsync(user => user.Id == request.UserId && user.IsActive, cancellationToken);
        if (targetUser is null)
        {
            ModelState.AddModelError(nameof(request.UserId), "Choose an active user account.");
            return ValidationProblem(ModelState);
        }

        var plan = TryOutSpotBillingCatalog.GetPlan(request.PlanCode);
        if (plan is null)
        {
            ModelState.AddModelError(nameof(request.PlanCode), $"'{request.PlanCode}' is not a supported plan.");
            return ValidationProblem(ModelState);
        }

        var targetRoles = await userManager.GetRolesAsync(targetUser);
        if (!TryOutSpotBillingCatalog.IsPlanEligibleForAccountTypes(plan.Code, targetRoles))
        {
            ModelState.AddModelError(nameof(request.PlanCode), "This plan is not available for the user's account type.");
            return ValidationProblem(ModelState);
        }

        var scope = ValidateGrantScope(plan.Code, request.ScopeType, request.ScopeId);
        if (scope is null)
        {
            return ValidationProblem(ModelState);
        }

        var startsAt = NormalizeUtc(request.StartsAt) ?? now;
        var endsAt = ResolveGrantEnd(request.DurationMonths, request.EndsAt, startsAt);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (endsAt is not null && endsAt <= startsAt)
        {
            ModelState.AddModelError(nameof(request.EndsAt), "The grant end date must be after the start date.");
            return ValidationProblem(ModelState);
        }

        if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length > 1000)
        {
            ModelState.AddModelError(nameof(request.Reason), "Keep the grant reason under 1000 characters.");
            return ValidationProblem(ModelState);
        }

        var overlapsExistingGrant = await dbContext.ComplimentaryPlanGrants
            .AsNoTracking()
            .AnyAsync(grant => grant.UserId == targetUser.Id
                && grant.PlanType == plan.Code
                && grant.ScopeType == scope.ScopeType
                && grant.ScopeId == scope.ScopeId
                && grant.RevokedAt == null
                && (endsAt == null || grant.StartsAt < endsAt)
                && (grant.EndsAt == null || grant.EndsAt > startsAt),
                cancellationToken);
        if (overlapsExistingGrant)
        {
            ModelState.AddModelError(nameof(request.PlanCode), "This user already has an overlapping complimentary grant for this plan and scope.");
            return ValidationProblem(ModelState);
        }

        var grant = new ComplimentaryPlanGrant
        {
            Id = Guid.NewGuid(),
            UserId = targetUser.Id,
            PlanType = plan.Code,
            ScopeType = scope.ScopeType,
            ScopeId = scope.ScopeId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Source = TryOutSpotPromotionCodes.AdminComplimentaryGrantSource,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            GrantedByUserId = adminUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ComplimentaryPlanGrants.Add(grant);
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            ComplimentaryPlanGrantMapper.ToResponse(grant, now));
    }

    [HttpPost("{grantId:guid}/revoke")]
    [ProducesResponseType<ComplimentaryPlanGrantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComplimentaryPlanGrantResponse>> RevokeGrant(
        Guid grantId,
        RevokeComplimentaryPlanGrantRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length > 1000)
        {
            ModelState.AddModelError(nameof(request.Reason), "Keep the revoke reason under 1000 characters.");
            return ValidationProblem(ModelState);
        }

        var grant = await dbContext.ComplimentaryPlanGrants
            .SingleOrDefaultAsync(currentGrant => currentGrant.Id == grantId, cancellationToken);
        if (grant is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        if (grant.RevokedAt is null)
        {
            grant.RevokedAt = now;
            grant.RevokedByUserId = adminUserId;
            grant.RevokeReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            grant.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(ComplimentaryPlanGrantMapper.ToResponse(grant, now));
    }

    private DateTime? ResolveGrantEnd(int? durationMonths, DateTime? requestedEndsAt, DateTime startsAt)
    {
        if (durationMonths is not null && requestedEndsAt is not null)
        {
            ModelState.AddModelError(nameof(CreateComplimentaryPlanGrantRequest.EndsAt), "Choose either durationMonths or endsAt, not both.");
            return null;
        }

        if (durationMonths is not null)
        {
            if (durationMonths is <= 0 or > 120)
            {
                ModelState.AddModelError(nameof(CreateComplimentaryPlanGrantRequest.DurationMonths), "Choose a duration between 1 and 120 months.");
                return null;
            }

            return startsAt.AddMonths(durationMonths.Value);
        }

        return NormalizeUtc(requestedEndsAt);
    }

    private GrantScope? ValidateGrantScope(string planCode, string? requestedScopeType, Guid? scopeId)
    {
        var scopeType = TryOutSpotSubscriptionScopeTypes.Normalize(requestedScopeType)
            ?? TryOutSpotSubscriptionScopeTypes.Account;
        if (!TryOutSpotBillingCatalog.IsSubscriptionScopeEligibleForPlan(planCode, scopeType))
        {
            ModelState.AddModelError(nameof(CreateComplimentaryPlanGrantRequest.ScopeType), "This scope is not available for the selected plan.");
            return null;
        }

        if (scopeType == TryOutSpotSubscriptionScopeTypes.Account && scopeId is not null)
        {
            ModelState.AddModelError(nameof(CreateComplimentaryPlanGrantRequest.ScopeId), "Account-scoped grants should not include a scope id.");
            return null;
        }

        if (scopeType != TryOutSpotSubscriptionScopeTypes.Account && scopeId is null)
        {
            ModelState.AddModelError(nameof(CreateComplimentaryPlanGrantRequest.ScopeId), "Choose the player, team, or organization this grant applies to.");
            return null;
        }

        return new GrantScope(scopeType, scopeId);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private sealed record GrantScope(string ScopeType, Guid? ScopeId);
}
