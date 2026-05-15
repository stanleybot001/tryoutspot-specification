using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models;
using TryOutSpot.Web.Models.Billing;

namespace TryOutSpot.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("/privacy-policy")]
    public IActionResult Privacy()
    {
        return View();
    }

    [HttpGet("/terms-and-conditions")]
    public IActionResult TermsAndConditions()
    {
        return View();
    }

    [HttpGet("/sms-consent")]
    public IActionResult SmsConsent()
    {
        return View();
    }

    [HttpGet("/plans-and-features")]
    public IActionResult PlansAndFeatures()
    {
        var featuresByCode = TryOutSpotBillingCatalog.Features
            .ToDictionary(feature => feature.Code, feature => new FeatureDetailPageItem(
                feature.Code,
                feature.Name,
                feature.Description),
                StringComparer.Ordinal);

        var freePlan = RequirePlan(TryOutSpotPlanCodes.FreePlayerParent);
        var premiumPlayerPlan = RequirePlan(TryOutSpotPlanCodes.PremiumPlayer);
        var teamBasicPlan = RequirePlan(TryOutSpotPlanCodes.TeamBasic);
        var teamOffseasonHoldPlan = RequirePlan(TryOutSpotPlanCodes.TeamOffseasonHold);
        var teamProfessionalPlan = RequirePlan(TryOutSpotPlanCodes.TeamProfessional);
        var enterprisePlan = RequirePlan(TryOutSpotPlanCodes.EnterpriseOrganization);

        var model = new PlansFeatureMatrixPageModel
        {
            AccountTypePlanAccess = BuildAccountTypePlanAccess(),
            FeatureBundles =
            [
                BuildBundle(
                    "Free Player/Parent baseline",
                    "Player/Parent",
                    "Base package",
                    [
                        TryOutSpotFeatureCodes.BrowseOpportunities,
                        TryOutSpotFeatureCodes.CreateBasicPlayerProfiles,
                        TryOutSpotFeatureCodes.ApplyToOpportunities,
                        TryOutSpotFeatureCodes.BasicTeamCommunication,
                        TryOutSpotFeatureCodes.ViewApplicationStatus
                    ],
                    featuresByCode),
                BuildBundle(
                    "Premium player add-ons",
                    "Player/Parent",
                    "Compared to Free Player/Parent",
                    [
                        TryOutSpotFeatureCodes.PriorityApplicationReview,
                        TryOutSpotFeatureCodes.AdvancedOpportunitySearch,
                        TryOutSpotFeatureCodes.EnhancedPlayerProfile,
                        TryOutSpotFeatureCodes.DirectTeamMessaging,
                        TryOutSpotFeatureCodes.PlayerApplicationAnalytics,
                        TryOutSpotFeatureCodes.EarlyOpportunityAccess
                    ],
                    featuresByCode),
                BuildBundle(
                    "Team basic starter bundle",
                    "Team/Academy",
                    "Entry paid team plan",
                    [
                        TryOutSpotFeatureCodes.PostLimitedOpportunities,
                        TryOutSpotFeatureCodes.BasicPlayerSearch,
                        TryOutSpotFeatureCodes.StandardRegistrationManagement,
                        TryOutSpotFeatureCodes.BasicTeamAnalytics,
                        TryOutSpotFeatureCodes.EmailSupport
                    ],
                    featuresByCode),
                BuildBundle(
                    "Team offseason hold",
                    "Team/Academy",
                    "Compared to Team Basic (retention option)",
                    [
                        TryOutSpotFeatureCodes.TeamDirectorySearchable,
                        TryOutSpotFeatureCodes.TeamContactHidden
                    ],
                    featuresByCode),
                BuildBundle(
                    "Team professional add-ons",
                    "Team/Academy",
                    "Compared to Team Basic",
                    [
                        TryOutSpotFeatureCodes.UnlimitedOpportunityPostings,
                        TryOutSpotFeatureCodes.AdvancedPlayerSearch,
                        TryOutSpotFeatureCodes.PremiumRegistrationManagement,
                        TryOutSpotFeatureCodes.DetailedTeamAnalytics,
                        TryOutSpotFeatureCodes.PrioritySupport,
                        TryOutSpotFeatureCodes.CustomBranding,
                        TryOutSpotFeatureCodes.BulkCommunication
                    ],
                    featuresByCode),
                BuildBundle(
                    "Enterprise organization add-ons",
                    "Organization",
                    "Compared to Team Professional",
                    [
                        TryOutSpotFeatureCodes.MultiTeamManagement,
                        TryOutSpotFeatureCodes.ApiAccess,
                        TryOutSpotFeatureCodes.CustomWorkflows,
                        TryOutSpotFeatureCodes.DedicatedAccountManager,
                        TryOutSpotFeatureCodes.WhiteLabel,
                        TryOutSpotFeatureCodes.AdvancedSecurity
                    ],
                    featuresByCode)
            ],
            PlanTracks =
            [
                new PlanTrackPageItem(
                    "Player/Parent track",
                    [
                        BuildPlanDetail(
                            freePlan,
                            previousPlan: null,
                            featuresByCode),
                        BuildPlanDetail(
                            premiumPlayerPlan,
                            previousPlan: freePlan,
                            featuresByCode)
                    ]),
                new PlanTrackPageItem(
                    "Team/Organization track",
                    [
                        BuildPlanDetail(
                            teamBasicPlan,
                            previousPlan: null,
                            featuresByCode),
                        BuildPlanDetail(
                            teamOffseasonHoldPlan,
                            previousPlan: teamBasicPlan,
                            featuresByCode),
                        BuildPlanDetail(
                            teamProfessionalPlan,
                            previousPlan: teamOffseasonHoldPlan,
                            featuresByCode),
                        BuildPlanDetail(
                            enterprisePlan,
                            previousPlan: teamProfessionalPlan,
                            featuresByCode)
                    ])
            ],
            CriticalPolicyNotes =
            [
                "Team Offseason Hold is only for Team Basic-eligible roles and keeps listings searchable while contact details stay hidden.",
                "Team Professional and Enterprise Organization are annual-commitment plans.",
                "If Team Professional or Enterprise Organization is canceled, team and organization records are soft-deactivated and cannot be reactivated for a later season. A new setup is required."
            ]
        };

        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private static BillingPlanDefinition RequirePlan(string planCode)
    {
        return TryOutSpotBillingCatalog.GetPlan(planCode)
            ?? throw new InvalidOperationException($"Plan '{planCode}' is not configured in billing catalog.");
    }

    private static FeatureBundlePageItem BuildBundle(
        string name,
        string scope,
        string comparedTo,
        IEnumerable<string> featureCodes,
        IReadOnlyDictionary<string, FeatureDetailPageItem> featuresByCode)
    {
        return new FeatureBundlePageItem(
            name,
            scope,
            comparedTo,
            featureCodes
                .Select(featureCode => featuresByCode[featureCode])
                .ToArray());
    }

    private static IReadOnlyCollection<AccountTypePlanAccessRow> BuildAccountTypePlanAccess()
    {
        var roles = new[]
        {
            TryOutSpotRoles.Parent,
            TryOutSpotRoles.Player,
            TryOutSpotRoles.Coach,
            TryOutSpotRoles.TeamManager,
            TryOutSpotRoles.AcademyDirector,
            TryOutSpotRoles.OrganizationAdmin
        };

        return roles
            .Select(role =>
            {
                var eligiblePlans = TryOutSpotBillingCatalog.GetEligiblePlanCodesForAccountTypes([role]);
                return new AccountTypePlanAccessRow(
                    role,
                    eligiblePlans.Contains(TryOutSpotPlanCodes.FreePlayerParent, StringComparer.Ordinal),
                    eligiblePlans.Contains(TryOutSpotPlanCodes.PremiumPlayer, StringComparer.Ordinal),
                    eligiblePlans.Contains(TryOutSpotPlanCodes.TeamBasic, StringComparer.Ordinal),
                    eligiblePlans.Contains(TryOutSpotPlanCodes.TeamOffseasonHold, StringComparer.Ordinal),
                    eligiblePlans.Contains(TryOutSpotPlanCodes.TeamProfessional, StringComparer.Ordinal),
                    eligiblePlans.Contains(TryOutSpotPlanCodes.EnterpriseOrganization, StringComparer.Ordinal));
            })
            .ToArray();
    }

    private static PlanDetailPageItem BuildPlanDetail(
        BillingPlanDefinition currentPlan,
        BillingPlanDefinition? previousPlan,
        IReadOnlyDictionary<string, FeatureDetailPageItem> featuresByCode)
    {
        var includedFeatures = currentPlan.IncludedFeatureCodes
            .Select(featureCode => featuresByCode[featureCode])
            .ToArray();

        var addedFeatureCodes = previousPlan is null
            ? currentPlan.IncludedFeatureCodes
            : currentPlan.IncludedFeatureCodes.Except(previousPlan.IncludedFeatureCodes, StringComparer.Ordinal);
        var addedFeatures = addedFeatureCodes
            .Select(featureCode => featuresByCode[featureCode])
            .ToArray();

        return new PlanDetailPageItem(
            currentPlan.Code,
            currentPlan.Name,
            currentPlan.Audience,
            currentPlan.Description,
            BuildPriceLabel(currentPlan),
            currentPlan.TrialDays,
            includedFeatures,
            addedFeatures);
    }

    private static string BuildPriceLabel(BillingPlanDefinition plan)
    {
        if (!plan.RequiresStripeSubscription || plan.MonthlyAmount <= 0m)
        {
            return "Free";
        }

        if (TryOutSpotBillingCatalog.RequiresAnnualCommitment(plan.Code) && plan.AnnualAmount is not null)
        {
            return $"{plan.AnnualAmount.Value:C2}/year (annual commitment)";
        }

        return plan.AnnualAmount is null
            ? $"{plan.MonthlyAmount:C2}/month"
            : $"{plan.MonthlyAmount:C2}/month or {plan.AnnualAmount.Value:C2}/year";
    }
}
