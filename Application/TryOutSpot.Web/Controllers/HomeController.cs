using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TryOutSpot.Web.Billing;
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

    [HttpGet("/account-deletion")]
    public IActionResult AccountDeletion()
    {
        return View();
    }

    [HttpGet("/plans-and-features")]
    public IActionResult PlansAndFeatures()
    {
        var model = new PlansFeatureMatrixPageModel
        {
            AccountTypePlanAccess = BuildAccountTypePlanAccess(),
            FeatureBundles = BuildFeatureBundlesFromMatrix(),
            PlanTracks = BuildPlanTracksFromMatrix(),
            CriticalPolicyNotes =
            [
                "Free Coach is an internal non-Stripe tier and allows one listing every six months.",
                "Paid plan checkout uses Stripe when keys and plan price IDs are configured.",
                "Team Professional and Enterprise Organization are annual-commitment plans.",
                "Paid plans canceled at period end keep features through the current billing term."
            ]
        };

        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private static IReadOnlyCollection<AccountTypePlanAccessRow> BuildAccountTypePlanAccess()
    {
        return
        [
            new("Parent", true, true, false, false, false, false),
            new("Player", true, true, false, false, false, false),
            new("Team representative (coach, manager)", false, false, true, true, true, true)
        ];
    }

    private static IReadOnlyCollection<FeatureBundlePageItem> BuildFeatureBundlesFromMatrix()
    {
        return
        [
            new FeatureBundlePageItem(
                "Free Player/Parent baseline",
                "Player/Parent",
                "Base package",
                [
                    F("opportunities.browse", "Browse opportunities", "Search and view public tryouts, tournaments, camps, and roster openings."),
                    F("opportunities.browse.free_rules", "Free discovery rules", "Age filtering is available, opportunity type is restricted to tryouts, geography radius is capped at 120 miles, and level is view-only (no level filter/sort)."),
                    F("players.profiles.basic", "Basic player profiles", "Create core player profiles for linked athletes."),
                    F("opportunities.apply", "Apply to opportunities", "Register or apply for available opportunities."),
                    F("communication.team.basic", "Basic team communication", "Receive and send basic opportunity-related communication."),
                    F("registrations.status.view", "Application status", "View registration and application status.")
                ]),
            new FeatureBundlePageItem(
                "Premium player add-ons",
                "Player/Parent",
                "Compared to Free Player/Parent",
                [
                    F("registrations.priority_review", "Priority application review", "Flag applications for higher visibility to teams."),
                    F("opportunities.search.advanced", "Advanced opportunity search", "Unlock enhanced geography radius beyond free limits, full opportunity-type filtering (not tryout-only), and advanced level-based discovery filters."),
                    F("players.profiles.enhanced", "Enhanced player profile", "Richer profiles with more detail, media, and highlight content."),
                    F("communication.team.direct", "Direct team messaging", "Coming soon - direct player-to-team messaging where allowed."),
                    F("registrations.analytics.player", "Player application analytics", "Coming soon - player-side application insights and tracking."),
                    F("opportunities.early_access", "Early opportunity access", "Eligible opportunities earlier than standard release.")
                ]),
            new FeatureBundlePageItem(
                "Free coach starter bundle",
                "Team/Academy",
                "No team plan",
                [
                    F("opportunities.post.limited", "Limited opportunity posting", "Publish up to 1 tryout listing every 6 months.")
                ]),
            new FeatureBundlePageItem(
                "Team basic add-ons",
                "Team/Academy",
                "Compared to Free Coach",
                [
                    F("opportunities.post.limited", "Limited opportunity posting", "Publish up to 9 tryout listings every 12 months."),
                    F("players.search.advanced", "Advanced player search", "Full player discovery filters including age, level, and radius."),
                    F("registrations.manage.standard", "Standard registration management", "Standard registration/applicant management."),
                    F("analytics.team.basic", "Basic team analytics", "Basic team activity reporting."),
                    F("support.email", "Email support", "Standard email support.")
                ]),
            new FeatureBundlePageItem(
                "Team professional add-ons",
                "Team/Academy",
                "Compared to Team Basic",
                [
                    F("opportunities.post.unlimited", "Professional opportunity posting cap", "Publish up to 24 tryout listings every 12 months."),
                    F("players.search.advanced", "Advanced player search", "Full player discovery filters including age, level, and radius."),
                    F("registrations.manage.premium", "Premium registration management", "Enhanced applicant review and registration tooling."),
                    F("analytics.team.detailed", "Detailed team analytics", "Deeper team analytics and conversion visibility."),
                    F("support.priority", "Priority support", "Priority support queue."),
                    F("branding.custom", "Custom branding", "Team branding customization."),
                    F("communication.bulk", "Bulk communication", "Bulk communication workflows.")
                ]),
            new FeatureBundlePageItem(
                "Enterprise organization add-ons",
                "Organization",
                "Compared to Team Professional",
                [
                    F("teams.manage.multiple", "Multi-team management", "Multi-team organization management."),
                    F("api.access", "API access", "API-based integration access."),
                    F("workflows.custom", "Custom workflows", "Organization-specific custom workflows."),
                    F("support.account_manager", "Dedicated account manager", "Dedicated account manager support."),
                    F("branding.white_label", "White-label options", "White-label options."),
                    F("security.advanced", "Advanced security", "Advanced security controls.")
                ])
        ];
    }

    private static IReadOnlyCollection<PlanTrackPageItem> BuildPlanTracksFromMatrix()
    {
        var freePlayerParent = new[]
        {
            F("opportunities.browse", "Browse opportunities", "Search and view public tryouts, tournaments, camps, and roster openings."),
            F("opportunities.browse.free_rules", "Free discovery rules", "Age filtering is available, opportunity type is restricted to tryouts, geography radius is capped at 120 miles, and level is view-only (no level filter/sort)."),
            F("players.profiles.basic", "Basic player profiles", "Create core player profiles for linked athletes."),
            F("opportunities.apply", "Apply to opportunities", "Register or apply for available opportunities."),
            F("communication.team.basic", "Basic team communication", "Receive and send basic opportunity-related communication."),
            F("registrations.status.view", "Application status", "View registration and application status.")
        };

        var premiumAdds = new[]
        {
            F("registrations.priority_review", "Priority application review", "Flag applications for higher visibility to teams."),
            F("opportunities.search.advanced", "Advanced opportunity search", "Unlock enhanced geography radius beyond free limits, full opportunity-type filtering (not tryout-only), and advanced level-based discovery filters."),
            F("players.profiles.enhanced", "Enhanced player profile", "Richer profiles with more detail, media, and highlight content."),
            F("communication.team.direct", "Direct team messaging", "Coming soon - direct player-to-team messaging where allowed."),
            F("registrations.analytics.player", "Player application analytics", "Coming soon - player-side application insights and tracking."),
            F("opportunities.early_access", "Early opportunity access", "Eligible opportunities earlier than standard release.")
        };

        var freeCoach = new[]
        {
            F("opportunities.post.limited", "Limited opportunity posting", "Publish up to 1 tryout listing every 6 months.")
        };

        var teamBasic = new[]
        {
            F("opportunities.post.limited", "Limited opportunity posting", "Publish up to 9 tryout listings every 12 months."),
            F("players.search.advanced", "Advanced player search", "Full player discovery filters including age, level, and radius."),
            F("registrations.manage.standard", "Standard registration management", "Standard registration/applicant management."),
            F("analytics.team.basic", "Basic team analytics", "Basic team activity reporting."),
            F("support.email", "Email support", "Standard email support.")
        };

        var pro = new[]
        {
            F("opportunities.post.unlimited", "Professional opportunity posting cap", "Publish up to 24 tryout listings every 12 months."),
            F("players.search.advanced", "Advanced player search", "Full player discovery filters including age, level, and radius."),
            F("registrations.manage.premium", "Premium registration management", "Enhanced applicant review and registration tooling."),
            F("analytics.team.detailed", "Detailed team analytics", "Deeper team analytics and conversion visibility."),
            F("support.priority", "Priority support", "Priority support queue."),
            F("branding.custom", "Custom branding", "Team branding customization."),
            F("communication.bulk", "Bulk communication", "Bulk communication workflows.")
        };

        var enterpriseAdds = new[]
        {
            F("teams.manage.multiple", "Multi-team management", "Multi-team organization management."),
            F("opportunities.post.enterprise", "Enterprise opportunity posting cap", "Publish up to 50 tryout listings every 12 months."),
            F("api.access", "API access", "API-based integration access."),
            F("workflows.custom", "Custom workflows", "Organization-specific custom workflows."),
            F("support.account_manager", "Dedicated account manager", "Dedicated account manager support."),
            F("branding.white_label", "White-label options", "White-label options."),
            F("security.advanced", "Advanced security", "Advanced security controls.")
        };

        return
        [
            new PlanTrackPageItem(
                "Player/Parent track",
                [
                    new PlanDetailPageItem(
                        TryOutSpotPlanCodes.FreePlayerParent,
                        "Free Player/Parent",
                        "Player/Parent",
                        "Always-free access for players, parents, and guardians.",
                        "Free",
                        null,
                        freePlayerParent,
                        freePlayerParent),
                    new PlanDetailPageItem(
                        TryOutSpotPlanCodes.PremiumPlayer,
                        "Premium Player",
                        "Player/Parent",
                        "Paid player profile, discovery, messaging, and analytics features.",
                        "$9.99/month or $99.00/year",
                        null,
                        freePlayerParent.Concat(premiumAdds).ToArray(),
                        premiumAdds)
                ]),
            new PlanTrackPageItem(
                "Team/Organization track",
                [
                    new PlanDetailPageItem(
                        TryOutSpotPlanCodes.FreeCoach,
                        "Free Coach",
                        "Team/Academy",
                        "Starter coach access with one tryout listing every six months.",
                        "Free",
                        null,
                        freeCoach,
                        freeCoach),
                    new PlanDetailPageItem(
                        TryOutSpotPlanCodes.TeamBasic,
                        "Basic Team",
                        "Team/Academy",
                        "Entry team subscription with limited annual tryout posting capacity.",
                        "$29.00/month",
                        null,
                        teamBasic,
                        teamBasic),
                    new PlanDetailPageItem(
                        TryOutSpotPlanCodes.TeamProfessional,
                        "Professional Team",
                        "Team/Academy",
                        "Professional team annual subscription for unlimited postings and advanced tools.",
                        "$799.00/year (annual commitment)",
                        null,
                        pro,
                        pro),
                    new PlanDetailPageItem(
                        TryOutSpotPlanCodes.EnterpriseOrganization,
                        "Enterprise Organization",
                        "Organization",
                        "Enterprise annual subscription for multi-team organizations and custom workflows.",
                        "$1,999.00/year (annual commitment)",
                        null,
                        pro.Concat(enterpriseAdds).ToArray(),
                        enterpriseAdds)
                ])
        ];
    }

    private static FeatureDetailPageItem F(string code, string name, string description)
    {
        return new FeatureDetailPageItem(code, name, description);
    }
}
