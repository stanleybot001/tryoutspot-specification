using TryOutSpot.Web.Identity;

namespace TryOutSpot.Web.Billing;

public static class TryOutSpotBillingCatalog
{
    private static readonly string[] PlayerParentFreeFeatures =
    [
        TryOutSpotFeatureCodes.BrowseOpportunities,
        TryOutSpotFeatureCodes.CreateBasicPlayerProfiles,
        TryOutSpotFeatureCodes.ApplyToOpportunities,
        TryOutSpotFeatureCodes.BasicTeamCommunication,
        TryOutSpotFeatureCodes.ViewApplicationStatus,
        TryOutSpotFeatureCodes.CreatePlayerListings
    ];

    private static readonly string[] FreeCoachFeatures =
    [
        TryOutSpotFeatureCodes.PostLimitedOpportunities,
        TryOutSpotFeatureCodes.ShareOpportunityListingLinks,
        TryOutSpotFeatureCodes.BasicPlayerSearch
    ];

    private static readonly BillingFeatureDefinition[] FeatureDefinitions =
    [
        new(
            TryOutSpotFeatureCodes.BrowseOpportunities,
            "Browse opportunities",
            "Search and view public tryouts, tournaments, camps, and roster openings.",
            false),
        new(
            TryOutSpotFeatureCodes.CreateBasicPlayerProfiles,
            "Basic player profiles",
            "Create core player profiles for linked athletes.",
            false),
        new(
            TryOutSpotFeatureCodes.ApplyToOpportunities,
            "Apply to opportunities",
            "Register or apply for available opportunities.",
            false),
        new(
            TryOutSpotFeatureCodes.BasicTeamCommunication,
            "Basic team communication",
            "Receive and send basic opportunity-related communication.",
            false),
        new(
            TryOutSpotFeatureCodes.ViewApplicationStatus,
            "Application status",
            "View registration and application status.",
            false),
        new(
            TryOutSpotFeatureCodes.CreatePlayerListings,
            "Player/parent listings",
            "Create and manage parent or player listings such as pickup-player availability, team-search notices, and used equipment posts.",
            false),
        new(
            TryOutSpotFeatureCodes.PriorityApplicationReview,
            "Priority application review",
            "Flag applications for higher visibility to teams.",
            true),
        new(
            TryOutSpotFeatureCodes.AdvancedOpportunitySearch,
            "Advanced opportunity search",
            "Use enhanced filters for geography, skill level, age, and opportunity type.",
            true),
        new(
            TryOutSpotFeatureCodes.EnhancedPlayerProfile,
            "Enhanced player profile",
            "Add richer profile details, media, performance data, and highlights.",
            true),
        new(
            TryOutSpotFeatureCodes.DirectTeamMessaging,
            "Direct team messaging",
            "Initiate direct messages with teams where allowed by safety rules.",
            true),
        new(
            TryOutSpotFeatureCodes.PlayerApplicationAnalytics,
            "Player application analytics",
            "See player-side application insights and activity history.",
            true),
        new(
            TryOutSpotFeatureCodes.EarlyOpportunityAccess,
            "Early opportunity access",
            "View eligible new opportunities before the standard release window.",
            true),
        new(
            TryOutSpotFeatureCodes.PostLimitedOpportunities,
            "Limited opportunity posting",
            "Publish team opportunity listings within the plan's rolling quota.",
            true),
        new(
            TryOutSpotFeatureCodes.ShareOpportunityListingLinks,
            "Share listing links",
            "Copy public listing links and ready-to-share post text for active opportunity listings.",
            false),
        new(
            TryOutSpotFeatureCodes.BasicPlayerSearch,
            "Basic player search",
            "Search player profiles with basic filters.",
            true),
        new(
            TryOutSpotFeatureCodes.StandardRegistrationManagement,
            "Standard registration management",
            "Review and manage opportunity registrations.",
            true),
        new(
            TryOutSpotFeatureCodes.FollowerSmsMessaging,
            "Follower SMS updates",
            "Coming soon - send short listing updates to opted-in players and parents who follow an opportunity.",
            true),
        new(
            TryOutSpotFeatureCodes.BasicTeamAnalytics,
            "Basic team analytics",
            "View basic opportunity and registration metrics.",
            true),
        new(
            TryOutSpotFeatureCodes.EmailSupport,
            "Email support",
            "Receive standard email support.",
            true),
        new(
            TryOutSpotFeatureCodes.TeamDirectorySearchable,
            "Searchable team directory listing",
            "Keep team or organization searchable in discovery results.",
            true),
        new(
            TryOutSpotFeatureCodes.TeamContactHidden,
            "Hidden public contact details",
            "Hide public contact links and social details while keeping directory visibility.",
            true),
        new(
            TryOutSpotFeatureCodes.UnlimitedOpportunityPostings,
            "Unlimited opportunity postings",
            "Post unlimited team opportunities.",
            true),
        new(
            TryOutSpotFeatureCodes.AdvancedPlayerSearch,
            "Advanced player search",
            "Use full player discovery filters including age, level, and radius.",
            true),
        new(
            TryOutSpotFeatureCodes.PremiumRegistrationManagement,
            "Premium registration management",
            "Use enhanced applicant review and registration management tools.",
            true),
        new(
            TryOutSpotFeatureCodes.DetailedTeamAnalytics,
            "Detailed team analytics",
            "View deeper opportunity, applicant, and conversion reporting.",
            true),
        new(
            TryOutSpotFeatureCodes.PrioritySupport,
            "Priority support",
            "Coming soon - receive priority customer support.",
            true),
        new(
            TryOutSpotFeatureCodes.CustomBranding,
            "Custom branding",
            "Coming soon - customize team or organization branding where supported.",
            true),
        new(
            TryOutSpotFeatureCodes.BulkCommunication,
            "Bulk communication",
            "Send bulk notifications and team messages.",
            true),
        new(
            TryOutSpotFeatureCodes.MultiTeamManagement,
            "Multi-team management",
            "Manage multiple teams under one organization account.",
            true),
        new(
            TryOutSpotFeatureCodes.ApiAccess,
            "API access",
            "Use approved external API integrations.",
            true),
        new(
            TryOutSpotFeatureCodes.CustomWorkflows,
            "Custom workflows",
            "Use organization-specific registration and management workflows.",
            true),
        new(
            TryOutSpotFeatureCodes.DedicatedAccountManager,
            "Dedicated account manager",
            "Receive dedicated account management support.",
            true),
        new(
            TryOutSpotFeatureCodes.WhiteLabel,
            "White-label options",
            "Use approved white-label experiences.",
            true),
        new(
            TryOutSpotFeatureCodes.AdvancedSecurity,
            "Advanced security",
            "Use enhanced security controls for larger organizations.",
            true)
    ];

    private static readonly BillingPlanDefinition[] PlanDefinitions =
    [
        new(
            TryOutSpotPlanCodes.FreePlayerParent,
            "Free Player/Parent",
            "Player/Parent",
            "Always-free access for players, parents, and guardians.",
            0m,
            null,
            "USD",
            null,
            false,
            PlayerParentFreeFeatures),
        new(
            TryOutSpotPlanCodes.PremiumPlayer,
            "Premium Player",
            "Player/Parent",
            "Paid player profile, discovery, messaging, and analytics features.",
            9.99m,
            99m,
            "USD",
            null,
            true,
            Add(PlayerParentFreeFeatures,
                TryOutSpotFeatureCodes.PriorityApplicationReview,
                TryOutSpotFeatureCodes.AdvancedOpportunitySearch,
                TryOutSpotFeatureCodes.EnhancedPlayerProfile,
                TryOutSpotFeatureCodes.DirectTeamMessaging,
                TryOutSpotFeatureCodes.PlayerApplicationAnalytics,
                TryOutSpotFeatureCodes.EarlyOpportunityAccess)),
        new(
            TryOutSpotPlanCodes.FreeCoach,
            "Free Coach",
            "Team/Academy",
            "Starter coach access with one tryout listing every six months.",
            0m,
            null,
            "USD",
            null,
            false,
            FreeCoachFeatures),
        new(
            TryOutSpotPlanCodes.TeamBasic,
            "Basic Team",
            "Team/Academy",
            "Entry team subscription with limited monthly postings.",
            29m,
            null,
            "USD",
            null,
            true,
            [
                TryOutSpotFeatureCodes.PostLimitedOpportunities,
                TryOutSpotFeatureCodes.BasicPlayerSearch,
                TryOutSpotFeatureCodes.AdvancedPlayerSearch,
                TryOutSpotFeatureCodes.StandardRegistrationManagement,
                TryOutSpotFeatureCodes.FollowerSmsMessaging,
                TryOutSpotFeatureCodes.BasicTeamAnalytics,
                TryOutSpotFeatureCodes.EmailSupport
            ]),
        new(
            TryOutSpotPlanCodes.TeamProfessional,
            "Professional Team",
            "Team/Academy",
            "Professional team annual subscription for unlimited postings and advanced tools.",
            79m,
            799m,
            "USD",
            null,
            true,
            [
                TryOutSpotFeatureCodes.UnlimitedOpportunityPostings,
                TryOutSpotFeatureCodes.AdvancedPlayerSearch,
                TryOutSpotFeatureCodes.FollowerSmsMessaging,
                TryOutSpotFeatureCodes.PremiumRegistrationManagement,
                TryOutSpotFeatureCodes.DetailedTeamAnalytics,
                TryOutSpotFeatureCodes.PrioritySupport,
                TryOutSpotFeatureCodes.CustomBranding,
                TryOutSpotFeatureCodes.BulkCommunication
            ]),
        new(
            TryOutSpotPlanCodes.EnterpriseOrganization,
            "Enterprise Organization",
            "Organization",
            "Enterprise annual subscription for multi-team organizations and custom workflows.",
            199m,
            1999m,
            "USD",
            null,
            true,
            [
                TryOutSpotFeatureCodes.UnlimitedOpportunityPostings,
                TryOutSpotFeatureCodes.AdvancedPlayerSearch,
                TryOutSpotFeatureCodes.FollowerSmsMessaging,
                TryOutSpotFeatureCodes.PremiumRegistrationManagement,
                TryOutSpotFeatureCodes.DetailedTeamAnalytics,
                TryOutSpotFeatureCodes.PrioritySupport,
                TryOutSpotFeatureCodes.CustomBranding,
                TryOutSpotFeatureCodes.BulkCommunication,
                TryOutSpotFeatureCodes.MultiTeamManagement,
                TryOutSpotFeatureCodes.ApiAccess,
                TryOutSpotFeatureCodes.CustomWorkflows,
                TryOutSpotFeatureCodes.DedicatedAccountManager,
                TryOutSpotFeatureCodes.WhiteLabel,
                TryOutSpotFeatureCodes.AdvancedSecurity
            ])
    ];

    public static IReadOnlyCollection<BillingFeatureDefinition> Features => FeatureDefinitions;

    public static IReadOnlyCollection<BillingPlanDefinition> Plans => PlanDefinitions;

    public static BillingPlanDefinition? GetPlan(string? planCode)
    {
        var normalizedPlanCode = NormalizePlanCode(planCode);
        return normalizedPlanCode is null
            ? null
            : PlanDefinitions.FirstOrDefault(plan => plan.Code == normalizedPlanCode);
    }

    public static string? NormalizePlanCode(string? planCode)
    {
        if (string.IsNullOrWhiteSpace(planCode))
        {
            return null;
        }

        var normalized = planCode.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
        return normalized switch
        {
            TryOutSpotPlanCodes.FreePlayerParent or "free" or "free_player" or "player_parent_free" =>
                TryOutSpotPlanCodes.FreePlayerParent,
            TryOutSpotPlanCodes.PremiumPlayer or "premium" or "player_premium" or "elite" =>
                TryOutSpotPlanCodes.PremiumPlayer,
            TryOutSpotPlanCodes.FreeCoach or "coach_free" =>
                TryOutSpotPlanCodes.FreeCoach,
            TryOutSpotPlanCodes.TeamBasic or "basic_team" or "team_trial" or "basic" =>
                TryOutSpotPlanCodes.TeamBasic,
            TryOutSpotPlanCodes.TeamProfessional or "professional_team" or "professional" or "pro" =>
                TryOutSpotPlanCodes.TeamProfessional,
            TryOutSpotPlanCodes.EnterpriseOrganization or "organization_enterprise" or "enterprise" =>
                TryOutSpotPlanCodes.EnterpriseOrganization,
            _ => null
        };
    }

    public static IReadOnlyCollection<string> GetFreeFeatureCodesForRoles(IEnumerable<string> accountTypes)
    {
        var features = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var accountType in accountTypes)
        {
            if (IsPlayerParentRole(accountType))
            {
                features.UnionWith(PlayerParentFreeFeatures);
            }

            if (IsTeamStarterRole(accountType))
            {
                features.UnionWith(FreeCoachFeatures);
            }
        }

        return features.ToArray();
    }

    public static IReadOnlyCollection<string> GetEligiblePlanCodesForAccountTypes(
        IEnumerable<string> accountTypes,
        bool includePlayerParentDefaultsWhenNoAccountTypes = false)
    {
        var roles = accountTypes
            .Select(TryOutSpotRoles.NormalizePublicRegistrationRole)
            .Where(role => role is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var planCodes = new List<string>();
        if (roles.Count == 0 && includePlayerParentDefaultsWhenNoAccountTypes)
        {
            planCodes.Add(TryOutSpotPlanCodes.FreePlayerParent);
            planCodes.Add(TryOutSpotPlanCodes.PremiumPlayer);
        }

        if (roles.Contains(TryOutSpotRoles.Parent) || roles.Contains(TryOutSpotRoles.Player))
        {
            planCodes.Add(TryOutSpotPlanCodes.FreePlayerParent);
            planCodes.Add(TryOutSpotPlanCodes.PremiumPlayer);
        }

        if (roles.Contains(TryOutSpotRoles.TeamRepresentative))
        {
            planCodes.Add(TryOutSpotPlanCodes.FreeCoach);
            planCodes.Add(TryOutSpotPlanCodes.TeamBasic);
            planCodes.Add(TryOutSpotPlanCodes.TeamProfessional);
            planCodes.Add(TryOutSpotPlanCodes.EnterpriseOrganization);
        }

        return planCodes
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static bool IsPlanEligibleForAccountTypes(string planCode, IEnumerable<string> accountTypes)
    {
        var normalizedPlanCode = NormalizePlanCode(planCode);
        return normalizedPlanCode is not null
            && GetEligiblePlanCodesForAccountTypes(accountTypes)
                .Contains(normalizedPlanCode, StringComparer.Ordinal);
    }

    public static bool IsSubscriptionScopeEligibleForPlan(string planCode, string scopeType)
    {
        var normalizedPlanCode = NormalizePlanCode(planCode);
        var normalizedScopeType = TryOutSpotSubscriptionScopeTypes.Normalize(scopeType);
        return normalizedPlanCode switch
        {
            TryOutSpotPlanCodes.PremiumPlayer =>
                normalizedScopeType is TryOutSpotSubscriptionScopeTypes.Account or TryOutSpotSubscriptionScopeTypes.Player,
            TryOutSpotPlanCodes.FreeCoach or TryOutSpotPlanCodes.TeamBasic or TryOutSpotPlanCodes.TeamProfessional =>
                normalizedScopeType is TryOutSpotSubscriptionScopeTypes.Account or TryOutSpotSubscriptionScopeTypes.Team,
            TryOutSpotPlanCodes.EnterpriseOrganization =>
                normalizedScopeType is TryOutSpotSubscriptionScopeTypes.Account or TryOutSpotSubscriptionScopeTypes.Organization,
            _ => false
        };
    }

    public static IReadOnlyCollection<string> GetSupportedBillingIntervals(string planCode)
    {
        var normalizedPlanCode = NormalizePlanCode(planCode);
        return normalizedPlanCode switch
        {
            TryOutSpotPlanCodes.FreePlayerParent => [BillingIntervalCodes.Month],
            TryOutSpotPlanCodes.FreeCoach => [BillingIntervalCodes.Month],
            TryOutSpotPlanCodes.PremiumPlayer => [BillingIntervalCodes.Month, BillingIntervalCodes.Year],
            TryOutSpotPlanCodes.TeamBasic => [BillingIntervalCodes.Month],
            TryOutSpotPlanCodes.TeamProfessional or TryOutSpotPlanCodes.EnterpriseOrganization => [BillingIntervalCodes.Year],
            _ => []
        };
    }

    public static bool IsBillingIntervalSupported(string planCode, string? billingInterval)
    {
        var normalizedInterval = BillingIntervalCodes.Normalize(billingInterval);
        if (normalizedInterval is null)
        {
            return false;
        }

        return GetSupportedBillingIntervals(planCode)
            .Contains(normalizedInterval, StringComparer.Ordinal);
    }

    public static bool RequiresAnnualCommitment(string planCode)
    {
        var normalizedPlanCode = NormalizePlanCode(planCode);
        return normalizedPlanCode is TryOutSpotPlanCodes.TeamProfessional or TryOutSpotPlanCodes.EnterpriseOrganization;
    }

    public static bool IsEntitlingSubscriptionStatus(string? status)
    {
        return string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "trialing", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlayerParentRole(string accountType)
    {
        return string.Equals(accountType, TryOutSpotRoles.Parent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(accountType, TryOutSpotRoles.Player, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTeamStarterRole(string accountType)
    {
        return string.Equals(accountType, TryOutSpotRoles.TeamRepresentative, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<string> Add(IReadOnlyCollection<string> existing, params string[] additional)
    {
        return existing.Concat(additional).ToArray();
    }
}
