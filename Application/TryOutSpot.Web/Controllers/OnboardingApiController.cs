using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Models.Onboarding;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Fast signup and first-run onboarding endpoints.
/// </summary>
[ApiController]
[Tags("Onboarding")]
[Produces("application/json")]
[Route("api/onboarding")]
public sealed class OnboardingApiController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    IEntitlementService entitlementService) : ControllerBase
{
    /// <summary>
    /// Returns public signup choices for the lightweight registration flow.
    /// </summary>
    [HttpGet("options")]
    [ProducesResponseType<OnboardingOptionsResponse>(StatusCodes.Status200OK)]
    public ActionResult<OnboardingOptionsResponse> GetOptions([FromQuery] string[]? accountTypes)
    {
        var normalizedAccountTypes = NormalizePublicAccountTypes(accountTypes ?? []);
        var plans = GetPlansForAccountTypes(normalizedAccountTypes);

        return Ok(new OnboardingOptionsResponse(
            GetAccountTypeOptions(),
            plans,
            CanSkipPlanSelection: true,
            "Create the account first, choose account type, then finish profiles or paid upgrades later."));
    }

    /// <summary>
    /// Returns the signed-in user's onboarding state and next recommended setup steps.
    /// </summary>
    [Authorize]
    [HttpGet("status")]
    [ProducesResponseType<OnboardingStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OnboardingStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is not { IsActive: true })
        {
            return Unauthorized();
        }

        var roles = (await userManager.GetRolesAsync(user)).OrderBy(role => role).ToArray();
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, cancellationToken);
        var steps = await BuildStepsAsync(user, roles, cancellationToken);
        var requiredStepsComplete = steps.Where(step => step.IsRequired).All(step => step.IsComplete);

        return Ok(new OnboardingStatusResponse(
            ToCurrentUserResponse(user, roles),
            steps,
            entitlements?.FeatureCodes ?? [],
            GetPlansForAccountTypes(roles),
            CanSkipPlanSelection: true,
            requiredStepsComplete));
    }

    /// <summary>
    /// Replaces the signed-in user's public account types.
    /// </summary>
    /// <remarks>
    /// This keeps signup forgiving: users can start quickly and adjust their account types later.
    /// Platform administrator roles are preserved and cannot be self-selected here.
    /// </remarks>
    [Authorize]
    [HttpPost("account-types")]
    [ProducesResponseType<OnboardingStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OnboardingStatusResponse>> UpdateAccountTypes(
        UpdateOnboardingAccountTypesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is not { IsActive: true })
        {
            return Unauthorized();
        }

        var accountTypes = GetValidatedPublicAccountTypes(request.AccountTypes, nameof(request.AccountTypes));
        if (accountTypes.Count == 0)
        {
            return ValidationProblem(ModelState);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var currentRoles = await userManager.GetRolesAsync(user);
        var publicRolesToRemove = currentRoles
            .Where(role => TryOutSpotRoles.PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (publicRolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, publicRolesToRemove);
            if (!removeResult.Succeeded)
            {
                AddIdentityErrors(removeResult);
                return ValidationProblem(ModelState);
            }
        }

        var addResult = await userManager.AddToRolesAsync(user, accountTypes);
        if (!addResult.Succeeded)
        {
            AddIdentityErrors(addResult);
            return ValidationProblem(ModelState);
        }

        user.UpdatedAt = DateTime.UtcNow;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            AddIdentityErrors(updateResult);
            return ValidationProblem(ModelState);
        }

        await transaction.CommitAsync(cancellationToken);

        return await GetStatus(cancellationToken);
    }

    private async Task<IReadOnlyCollection<OnboardingStepResponse>> BuildStepsAsync(
        User user,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var hasPlayerOrParentRole = HasAnyRole(roles, TryOutSpotRoles.Parent, TryOutSpotRoles.Player);
        var hasTeamOrOrganizationRole = HasAnyRole(
            roles,
            TryOutSpotRoles.Coach,
            TryOutSpotRoles.TeamManager,
            TryOutSpotRoles.AcademyDirector,
            TryOutSpotRoles.OrganizationAdmin);

        var hasLinkedPlayers = hasPlayerOrParentRole
            && await dbContext.UserPlayerRelationships
                .AsNoTracking()
                .AnyAsync(relationship => relationship.UserId == user.Id, cancellationToken);

        var hasTeamRole = hasTeamOrOrganizationRole
            && await dbContext.UserTeamRoles
                .AsNoTracking()
                .AnyAsync(teamRole => teamRole.UserId == user.Id, cancellationToken);

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(subscription => subscription.UserId == user.Id, cancellationToken);
        var subscriptionPlan = TryOutSpotBillingCatalog.GetPlan(subscription?.PlanType);
        var hasPaidPlan = subscriptionPlan?.RequiresStripeSubscription == true
            && TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription?.Status);

        var hasPublicAccountType = roles.Any(role =>
            TryOutSpotRoles.PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase));

        var steps = new List<OnboardingStepResponse>
        {
            new(
                "choose_account_types",
                "Choose account type",
                "Select how you plan to use TryOutSpot. You can choose more than one.",
                IsRequired: true,
                IsComplete: hasPublicAccountType,
                "/api/onboarding/account-types"),
            new(
                "verify_email",
                "Verify email",
                "Verify the account email address before using password sign-in and sensitive account features.",
                IsRequired: true,
                IsComplete: user.EmailConfirmed,
                "/api/account/resend-email-verification")
        };

        if (hasPlayerOrParentRole)
        {
            steps.Add(new OnboardingStepResponse(
                "add_player_profile",
                "Add player profile",
                "Add a player profile when you are ready to register for tryouts or improve matching.",
                IsRequired: false,
                IsComplete: hasLinkedPlayers,
                null));
        }

        if (hasTeamOrOrganizationRole)
        {
            steps.Add(new OnboardingStepResponse(
                "add_team_or_organization",
                "Add team or organization",
                "Create or join a team or organization before posting opportunities.",
                IsRequired: false,
                IsComplete: hasTeamRole,
                null));
        }

        if (GetPlansForAccountTypes(roles).Any(plan => plan.RequiresStripeSubscription))
        {
            steps.Add(new OnboardingStepResponse(
                "choose_plan",
                "Choose plan",
                "Start free and upgrade when you need premium profile, team, or organization tools.",
                IsRequired: false,
                IsComplete: hasPaidPlan,
                "/api/billing/plans"));
        }

        return steps;
    }

    private static IReadOnlyCollection<OnboardingAccountTypeOptionResponse> GetAccountTypeOptions()
    {
        return
        [
            new(
                TryOutSpotRoles.Parent,
                "Parent or guardian",
                "Find tryouts, manage child player profiles, and track registrations.",
                true),
            new(
                TryOutSpotRoles.Player,
                "Player",
                "Build a profile and discover baseball or softball opportunities.",
                true),
            new(
                TryOutSpotRoles.Coach,
                "Coach",
                "Find players, manage tryouts, and communicate with families.",
                true),
            new(
                TryOutSpotRoles.TeamManager,
                "Team manager",
                "Help operate a team, registrations, and opportunity listings.",
                false),
            new(
                TryOutSpotRoles.AcademyDirector,
                "Academy director",
                "Manage academy-level teams, listings, and player development programs.",
                false),
            new(
                TryOutSpotRoles.OrganizationAdmin,
                "Organization admin",
                "Manage multi-team organizations, staff, and platform workflows.",
                false)
        ];
    }

    private static IReadOnlyCollection<BillingPlanResponse> GetPlansForAccountTypes(IEnumerable<string> accountTypes)
    {
        return TryOutSpotBillingCatalog.GetEligiblePlanCodesForAccountTypes(
                accountTypes,
                includePlayerParentDefaultsWhenNoAccountTypes: true)
            .Select(TryOutSpotBillingCatalog.GetPlan)
            .Where(plan => plan is not null)
            .Cast<BillingPlanDefinition>()
            .Select(ToBillingPlanResponse)
            .ToArray();
    }

    private List<string> GetValidatedPublicAccountTypes(
        IReadOnlyCollection<string>? requestedAccountTypes,
        string modelStateKey)
    {
        if (requestedAccountTypes is null || requestedAccountTypes.Count == 0)
        {
            ModelState.AddModelError(modelStateKey, "At least one account type is required.");
            return [];
        }

        var accountTypes = new List<string>();
        foreach (var requestedAccountType in requestedAccountTypes)
        {
            var accountType = TryOutSpotRoles.NormalizePublicRegistrationRole(requestedAccountType);
            if (accountType is null)
            {
                ModelState.AddModelError(
                    modelStateKey,
                    $"'{requestedAccountType}' is not a supported public account type.");

                continue;
            }

            if (!accountTypes.Contains(accountType, StringComparer.OrdinalIgnoreCase))
            {
                accountTypes.Add(accountType);
            }
        }

        return accountTypes;
    }

    private static IReadOnlyCollection<string> NormalizePublicAccountTypes(IEnumerable<string> accountTypes)
    {
        return accountTypes
            .Select(TryOutSpotRoles.NormalizePublicRegistrationRole)
            .Where(role => role is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var userId)
            ? await userManager.FindByIdAsync(userId.ToString())
            : null;
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }
    }

    private static bool HasAnyRole(IReadOnlyCollection<string> roles, params string[] candidates)
    {
        return roles.Any(role => candidates.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    private static CurrentUserResponse ToCurrentUserResponse(User user, IReadOnlyCollection<string> accountTypes)
    {
        return new CurrentUserResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            accountTypes,
            user.IsActive,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.SmsConsentAccepted,
            user.SmsConsentAcceptedAt);
    }

    private static BillingPlanResponse ToBillingPlanResponse(BillingPlanDefinition plan)
    {
        return new BillingPlanResponse(
            plan.Code,
            plan.Name,
            plan.Audience,
            plan.Description,
            plan.MonthlyAmount,
            plan.AnnualAmount,
            plan.Currency,
            plan.TrialDays,
            plan.RequiresStripeSubscription,
            plan.IncludedFeatureCodes);
    }
}
