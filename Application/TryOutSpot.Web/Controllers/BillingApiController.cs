using Microsoft.AspNetCore.Mvc;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Models.Billing;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Billing plans, feature catalog, and entitlement reference endpoints.
/// </summary>
[ApiController]
[Tags("Billing")]
[Produces("application/json")]
[Route("api/billing")]
public sealed class BillingApiController : ControllerBase
{
    /// <summary>
    /// Lists the public billing plans and included feature codes.
    /// </summary>
    /// <response code="200">Returns the current billing plan catalog.</response>
    [HttpGet("plans")]
    [ProducesResponseType<BillingPlanResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<BillingPlanResponse>> GetPlans()
    {
        var plans = TryOutSpotBillingCatalog.Plans
            .Select(plan => new BillingPlanResponse(
                plan.Code,
                plan.Name,
                plan.Audience,
                plan.Description,
                plan.MonthlyAmount,
                plan.AnnualAmount,
                plan.Currency,
                plan.TrialDays,
                plan.RequiresStripeSubscription,
                plan.IncludedFeatureCodes))
            .ToArray();

        return Ok(plans);
    }

    /// <summary>
    /// Lists all known feature codes used for free and paid entitlement checks.
    /// </summary>
    /// <response code="200">Returns the current billing feature catalog.</response>
    [HttpGet("features")]
    [ProducesResponseType<BillingFeatureResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<BillingFeatureResponse>> GetFeatures()
    {
        var features = TryOutSpotBillingCatalog.Features
            .Select(feature => new BillingFeatureResponse(
                feature.Code,
                feature.Name,
                feature.Description,
                feature.IsPaidFeature))
            .ToArray();

        return Ok(features);
    }
}
