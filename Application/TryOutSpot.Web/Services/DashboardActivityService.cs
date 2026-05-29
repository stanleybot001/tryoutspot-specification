using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Dashboard;

namespace TryOutSpot.Web.Services;

public sealed class DashboardActivityService(
    AppDbContext dbContext,
    IEntitlementService entitlementService) : IDashboardActivityService
{
    private const int DefaultFirstVisitLookbackDays = 14;
    private const string OpportunityItemKind = "opportunity";
    private const string PlayerListingItemKind = "player_listing";
    private const string PublicVisibility = "Public";
    private const string CoachOnlyVisibility = "VerifiedCoachesOnly";

    private static readonly ActivityOptionDefinition[] OptionDefinitions =
    [
        new(
            DashboardActivityTypeCodes.Tryouts,
            "Tryouts",
            "New public tryout opportunities.",
            "Player/Parent",
            AppliesToPlayerParent: true,
            AppliesToTeam: false,
            RequiredFeatureCodes: [TryOutSpotFeatureCodes.BrowseOpportunities],
            IsDefaultSelected: true),
        new(
            DashboardActivityTypeCodes.Tournaments,
            "Tournaments",
            "New tournament and event opportunities.",
            "Player/Parent",
            AppliesToPlayerParent: true,
            AppliesToTeam: false,
            RequiredFeatureCodes: [TryOutSpotFeatureCodes.AdvancedOpportunitySearch],
            IsDefaultSelected: true),
        new(
            DashboardActivityTypeCodes.PickupOpportunities,
            "Pickup opportunities",
            "New pickup-player needs and player availability posts.",
            "Player/Parent",
            AppliesToPlayerParent: true,
            AppliesToTeam: false,
            RequiredFeatureCodes: [TryOutSpotFeatureCodes.AdvancedOpportunitySearch],
            IsDefaultSelected: true),
        new(
            DashboardActivityTypeCodes.ForSaleItems,
            "For sale items",
            "New used equipment listings from the community.",
            "Player/Parent",
            AppliesToPlayerParent: true,
            AppliesToTeam: false,
            RequiredFeatureCodes: [TryOutSpotFeatureCodes.AdvancedOpportunitySearch],
            IsDefaultSelected: true),
        new(
            DashboardActivityTypeCodes.RosterOpenings,
            "Roster openings",
            "New team roster openings and looking-for-player posts.",
            "Player/Parent",
            AppliesToPlayerParent: true,
            AppliesToTeam: false,
            RequiredFeatureCodes: [TryOutSpotFeatureCodes.AdvancedOpportunitySearch],
            IsDefaultSelected: true),
        new(
            DashboardActivityTypeCodes.CampsAndClinics,
            "Camps and clinics",
            "New camp, clinic, and private workout opportunities.",
            "Player/Parent",
            AppliesToPlayerParent: true,
            AppliesToTeam: false,
            RequiredFeatureCodes: [TryOutSpotFeatureCodes.AdvancedOpportunitySearch],
            IsDefaultSelected: true),
        new(
            DashboardActivityTypeCodes.TeamNewListings,
            "New player listings",
            "New published player listings for team representatives.",
            "Team",
            AppliesToPlayerParent: false,
            AppliesToTeam: true,
            RequiredFeatureCodes: [TryOutSpotFeatureCodes.BasicPlayerSearch, TryOutSpotFeatureCodes.AdvancedPlayerSearch],
            IsDefaultSelected: true,
            RequireAnyFeature: true)
    ];

    public async Task<DashboardActivityPreferencesResponse> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entitlements = await entitlementService.GetEntitlementsAsync(userId, cancellationToken);
        var preference = await GetPreferenceAsync(userId, cancellationToken);
        return BuildPreferencesResponse(preference, entitlements);
    }

    public async Task<DashboardActivityPreferencesResponse> UpdatePreferencesAsync(
        Guid userId,
        IReadOnlyCollection<string> activityTypes,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var preference = await GetPreferenceAsync(userId, cancellationToken);
        if (preference is null)
        {
            preference = new UserDashboardPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.UserDashboardPreferences.Add(preference);
        }

        var selectedActivityTypes = NormalizeActivityTypes(activityTypes);
        preference.ActivityTypesJson = JsonSerializer.Serialize(selectedActivityTypes);
        preference.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var entitlements = await entitlementService.GetEntitlementsAsync(userId, cancellationToken);
        return BuildPreferencesResponse(preference, entitlements);
    }

    public async Task<DashboardRecentActivityResponse> GetRecentActivityAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var entitlements = await entitlementService.GetEntitlementsAsync(userId, cancellationToken);
        var preference = await GetPreferenceAsync(userId, cancellationToken);
        var effectiveActivityTypes = ResolveEffectiveActivityTypes(preference, entitlements).ToHashSet(StringComparer.Ordinal);
        var previousViewedAt = preference?.LastViewedAt;
        var since = previousViewedAt ?? now.AddDays(-DefaultFirstVisitLookbackDays);

        var sectionSources = new List<DashboardActivitySectionSource>();
        if (ContainsAny(
            effectiveActivityTypes,
            DashboardActivityTypeCodes.Tryouts,
            DashboardActivityTypeCodes.Tournaments,
            DashboardActivityTypeCodes.PickupOpportunities,
            DashboardActivityTypeCodes.RosterOpenings,
            DashboardActivityTypeCodes.CampsAndClinics))
        {
            var opportunityCount = await CountOpportunityActivityItemsAsync(
                effectiveActivityTypes,
                since,
                now,
                cancellationToken);
            sectionSources.Add(new DashboardActivitySectionSource(
                "player_opportunities",
                "New opportunities",
                "No new opportunities match your dashboard settings yet.",
                opportunityCount,
                (take, token) => GetOpportunityActivityItemsAsync(
                    effectiveActivityTypes,
                    since,
                    now,
                    take,
                    token)));
        }

        if (ContainsAny(
            effectiveActivityTypes,
            DashboardActivityTypeCodes.PickupOpportunities,
            DashboardActivityTypeCodes.ForSaleItems))
        {
            var parentListingCount = await CountPlayerListingActivityItemsAsync(
                userId,
                effectiveActivityTypes,
                includeTeamRelevantListings: false,
                since,
                now,
                cancellationToken);
            sectionSources.Add(new DashboardActivitySectionSource(
                "player_parent_listings",
                "New community listings",
                "No new pickup or equipment listings match your dashboard settings yet.",
                parentListingCount,
                (take, token) => GetPlayerListingActivityItemsAsync(
                    userId,
                    effectiveActivityTypes,
                    includeTeamRelevantListings: false,
                    since,
                    now,
                    take,
                    token)));
        }

        if (effectiveActivityTypes.Contains(DashboardActivityTypeCodes.TeamNewListings))
        {
            var teamListingCount = await CountPlayerListingActivityItemsAsync(
                userId,
                effectiveActivityTypes,
                includeTeamRelevantListings: true,
                since,
                now,
                cancellationToken);
            sectionSources.Add(new DashboardActivitySectionSource(
                "team_new_listings",
                "New player listings",
                "No new player listings have been added in this window.",
                teamListingCount,
                (take, token) => GetPlayerListingActivityItemsAsync(
                    userId,
                    effectiveActivityTypes,
                    includeTeamRelevantListings: true,
                    since,
                    now,
                    take,
                    token)));
        }

        var totalCount = sectionSources.Sum(section => section.TotalCount);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)normalizedPageSize));
        var normalizedPage = Math.Clamp(page, 1, totalPages);
        var skip = (normalizedPage - 1) * normalizedPageSize;
        var takePerSection = skip + normalizedPageSize;
        var loadedItems = new List<DashboardActivityPagedItem>();
        foreach (var source in sectionSources.Where(source => source.TotalCount > 0))
        {
            var sourceItems = await source.LoadItemsAsync(takePerSection, cancellationToken);
            loadedItems.AddRange(sourceItems.Select(item => new DashboardActivityPagedItem(source.Code, item)));
        }

        var pagedItems = loadedItems
            .OrderByDescending(item => item.Item.ActivityAt)
            .ThenBy(item => item.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(normalizedPageSize)
            .ToArray();
        var pagedItemsBySection = pagedItems
            .GroupBy(item => item.SectionCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Item).ToArray() as IReadOnlyCollection<DashboardActivityItemResponse>,
                StringComparer.Ordinal);
        var sections = sectionSources
            .Select(source => new DashboardActivitySectionResponse(
                source.Code,
                source.Title,
                source.EmptyMessage,
                source.TotalCount,
                pagedItemsBySection.TryGetValue(source.Code, out var items) ? items : []))
            .ToArray();

        return new DashboardRecentActivityResponse(
            now,
            since,
            previousViewedAt,
            effectiveActivityTypes.OrderBy(type => type, StringComparer.Ordinal).ToArray(),
            normalizedPage,
            normalizedPageSize,
            totalCount,
            totalPages,
            sections);
    }

    public async Task<DashboardActivityViewedResponse> MarkViewedAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var preference = await GetPreferenceAsync(userId, cancellationToken);
        if (preference is null)
        {
            preference = new UserDashboardPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = now
            };
            dbContext.UserDashboardPreferences.Add(preference);
        }

        preference.LastViewedAt = now;
        preference.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DashboardActivityViewedResponse(now);
    }

    private async Task<IReadOnlyCollection<DashboardActivityItemResponse>> GetOpportunityActivityItemsAsync(
        IReadOnlySet<string> effectiveActivityTypes,
        DateTime since,
        DateTime now,
        int take,
        CancellationToken cancellationToken)
    {
        var opportunities = await BuildOpportunityActivityQuery(effectiveActivityTypes, since, now)
            .Include(opportunity => opportunity.Sport)
            .Include(opportunity => opportunity.Team)
                .ThenInclude(team => team.Organization)
            .OrderByDescending(opportunity => opportunity.PublishedAt ?? opportunity.UpdatedAt)
            .ThenByDescending(opportunity => opportunity.UpdatedAt)
            .Take(take)
            .ToArrayAsync(cancellationToken);

        return opportunities
            .Select(opportunity =>
            {
                var activityType = ResolveOpportunityActivityType(opportunity.Type);
                var teamName = opportunity.Team.Name;
                var organizationName = opportunity.Team.Organization?.Name;
                var subtitle = string.IsNullOrWhiteSpace(organizationName)
                    ? teamName
                    : $"{teamName} / {organizationName}";
                var eventDetail = opportunity.EventDate.HasValue
                    ? $"Event {opportunity.EventDate.Value.ToLocalTime():MMM d, yyyy}"
                    : opportunity.RegistrationDeadline.HasValue
                        ? $"Register by {opportunity.RegistrationDeadline.Value.ToLocalTime():MMM d, yyyy}"
                        : opportunity.Sport.Name;

                return new DashboardActivityItemResponse(
                    activityType,
                    GetActivityTypeLabel(activityType),
                    OpportunityItemKind,
                    opportunity.Id,
                    opportunity.Title,
                    subtitle,
                    eventDetail,
                    FormatLocation(opportunity.City ?? opportunity.Team.City, opportunity.State ?? opportunity.Team.State),
                    $"/opportunities/{opportunity.Id}",
                    MaxDate(opportunity.PublishedAt, opportunity.UpdatedAt, opportunity.CreatedAt));
            })
            .ToArray();
    }

    private async Task<int> CountOpportunityActivityItemsAsync(
        IReadOnlySet<string> effectiveActivityTypes,
        DateTime since,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return await BuildOpportunityActivityQuery(effectiveActivityTypes, since, now)
            .CountAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<DashboardActivityItemResponse>> GetPlayerListingActivityItemsAsync(
        Guid userId,
        IReadOnlySet<string> effectiveActivityTypes,
        bool includeTeamRelevantListings,
        DateTime since,
        DateTime now,
        int take,
        CancellationToken cancellationToken)
    {
        var listings = await BuildPlayerListingActivityQuery(
                userId,
                effectiveActivityTypes,
                includeTeamRelevantListings,
                since,
                now)
            .Include(listing => listing.Player)
            .Include(listing => listing.Sport)
            .OrderByDescending(listing => listing.PublishedAt ?? listing.UpdatedAt)
            .ThenByDescending(listing => listing.UpdatedAt)
            .Take(take)
            .ToArrayAsync(cancellationToken);

        return listings
            .Select(listing =>
            {
                var activityType = ResolvePlayerListingActivityType(listing.ListingType, includeTeamRelevantListings);
                var playerName = listing.Player is null
                    ? null
                    : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim();
                var detail = listing.AskingPrice.HasValue
                    ? $"{listing.AskingPrice.Value:C0} {listing.Currency ?? "USD"}"
                    : listing.Sport?.Name;

                return new DashboardActivityItemResponse(
                    activityType,
                    GetActivityTypeLabel(activityType),
                    PlayerListingItemKind,
                    listing.Id,
                    listing.Title,
                    playerName,
                    detail,
                    FormatLocation(listing.City, listing.State),
                    $"/player-listings/{listing.Id}",
                    MaxDate(listing.PublishedAt, listing.UpdatedAt, listing.CreatedAt));
            })
            .ToArray();
    }

    private async Task<int> CountPlayerListingActivityItemsAsync(
        Guid userId,
        IReadOnlySet<string> effectiveActivityTypes,
        bool includeTeamRelevantListings,
        DateTime since,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return await BuildPlayerListingActivityQuery(
                userId,
                effectiveActivityTypes,
                includeTeamRelevantListings,
                since,
                now)
            .CountAsync(cancellationToken);
    }

    private IQueryable<Opportunity> BuildOpportunityActivityQuery(
        IReadOnlySet<string> effectiveActivityTypes,
        DateTime since,
        DateTime now)
    {
        var includeTryouts = effectiveActivityTypes.Contains(DashboardActivityTypeCodes.Tryouts);
        var includeTournaments = effectiveActivityTypes.Contains(DashboardActivityTypeCodes.Tournaments);
        var includePickup = effectiveActivityTypes.Contains(DashboardActivityTypeCodes.PickupOpportunities);
        var includeRosterOpenings = effectiveActivityTypes.Contains(DashboardActivityTypeCodes.RosterOpenings);
        var includeCampsAndClinics = effectiveActivityTypes.Contains(DashboardActivityTypeCodes.CampsAndClinics);

        return dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => opportunity.IsActive)
            .Where(opportunity => opportunity.IsPublished)
            .Where(opportunity =>
                (opportunity.ListingEndDate ?? opportunity.ExpiresAt) == null
                || (opportunity.ListingEndDate ?? opportunity.ExpiresAt) > now)
            .Where(opportunity => opportunity.ListingStartDate == null || opportunity.ListingStartDate <= now)
            .Where(opportunity => opportunity.Team.IsActive)
            .Where(opportunity => opportunity.Team.IsSearchable)
            .Where(opportunity =>
                (opportunity.PublishedAt ?? opportunity.CreatedAt) >= since
                || opportunity.UpdatedAt >= since)
            .Where(opportunity =>
                (includeTryouts && opportunity.Type.ToLower().Contains("tryout"))
                || (includeTournaments && opportunity.Type.ToLower().Contains("tournament"))
                || (includePickup && opportunity.Type.ToLower().Contains("pickup"))
                || (includeRosterOpenings && (opportunity.Type.ToLower().Contains("roster") || opportunity.Type.ToLower().Contains("opening")))
                || (includeCampsAndClinics && (opportunity.Type.ToLower().Contains("camp")
                    || opportunity.Type.ToLower().Contains("clinic")
                    || opportunity.Type.ToLower().Contains("workout"))));
    }

    private IQueryable<PlayerListing> BuildPlayerListingActivityQuery(
        Guid userId,
        IReadOnlySet<string> effectiveActivityTypes,
        bool includeTeamRelevantListings,
        DateTime since,
        DateTime now)
    {
        var includePickup = effectiveActivityTypes.Contains(DashboardActivityTypeCodes.PickupOpportunities);
        var includeForSale = effectiveActivityTypes.Contains(DashboardActivityTypeCodes.ForSaleItems);

        return dbContext.PlayerListings
            .AsNoTracking()
            .Where(listing => listing.UserId != userId)
            .Where(listing => listing.IsActive)
            .Where(listing => listing.IsPublished)
            .Where(listing => listing.IsSearchable)
            .Where(listing => listing.ExpiresAt == null || listing.ExpiresAt > now)
            .Where(listing =>
                (listing.PublishedAt ?? listing.CreatedAt) >= since
                || listing.UpdatedAt >= since)
            .Where(listing =>
                listing.Player == null
                || (listing.Player.IsActive
                    && listing.Player.IsSearchable
                    && (listing.Player.ContactVisibility == PublicVisibility
                        || (includeTeamRelevantListings && listing.Player.ContactVisibility == CoachOnlyVisibility))))
            .Where(listing =>
                (includePickup && listing.ListingType == TryOutSpotPlayerListingTypes.PickupPlayer)
                || (includeForSale && listing.ListingType == TryOutSpotPlayerListingTypes.UsedEquipment)
                || (includeTeamRelevantListings
                    && (listing.ListingType == TryOutSpotPlayerListingTypes.LookingForTeam
                        || listing.ListingType == TryOutSpotPlayerListingTypes.PickupPlayer
                        || listing.ListingType == TryOutSpotPlayerListingTypes.TrainingPartner
                        || listing.ListingType == TryOutSpotPlayerListingTypes.PrivateLessons)));
    }

    private DashboardActivityPreferencesResponse BuildPreferencesResponse(
        UserDashboardPreference? preference,
        UserEntitlementSet? entitlements)
    {
        var selectedActivityTypes = ResolveEffectiveActivityTypes(preference, entitlements).ToHashSet(StringComparer.Ordinal);
        var options = BuildOptionStates(entitlements, selectedActivityTypes)
            .Select(option => new DashboardActivityPreferenceOptionResponse(
                option.Definition.Code,
                option.Definition.Label,
                option.Definition.Description,
                option.Definition.Audience,
                selectedActivityTypes.Contains(option.Definition.Code),
                option.IsAvailable,
                option.UnavailableReason))
            .ToArray();

        return new DashboardActivityPreferencesResponse(
            preference?.LastViewedAt,
            selectedActivityTypes.OrderBy(type => type, StringComparer.Ordinal).ToArray(),
            options);
    }

    private IReadOnlyCollection<string> ResolveEffectiveActivityTypes(
        UserDashboardPreference? preference,
        UserEntitlementSet? entitlements)
    {
        var optionStates = BuildOptionStates(entitlements, selectedActivityTypes: null).ToArray();
        var availableCodes = optionStates
            .Where(option => option.IsAvailable)
            .Select(option => option.Definition.Code)
            .ToHashSet(StringComparer.Ordinal);

        if (availableCodes.Count == 0)
        {
            return [];
        }

        var preferredCodes = preference is null || preference.ActivityTypesJson is null
            ? optionStates
                .Where(option => option.IsAvailable)
                .Where(option => option.Definition.IsDefaultSelected)
                .Select(option => option.Definition.Code)
                .ToArray()
            : ParseActivityTypes(preference.ActivityTypesJson);

        return preferredCodes
            .Where(availableCodes.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyCollection<ActivityOptionState> BuildOptionStates(
        UserEntitlementSet? entitlements,
        IReadOnlySet<string>? selectedActivityTypes)
    {
        var accountTypes = entitlements?.AccountTypes ?? [];
        var featureCodes = entitlements?.FeatureCodes ?? [];
        var hasPlayerParentRole = accountTypes.Any(IsPlayerParentRole);
        var hasTeamRole = accountTypes.Any(TryOutSpotRoles.IsTeamBundleRole);
        var applicableDefinitions = OptionDefinitions
            .Where(definition => (definition.AppliesToPlayerParent && hasPlayerParentRole)
                || (definition.AppliesToTeam && hasTeamRole))
            .ToArray();

        return applicableDefinitions
            .Select(definition =>
            {
                var isAvailable = HasRequiredFeatures(definition, featureCodes);
                var unavailableReason = isAvailable
                    ? null
                    : definition.Code == DashboardActivityTypeCodes.Tryouts
                        ? "Requires opportunity browsing access."
                        : definition.AppliesToTeam
                            ? "Requires player search access for team accounts."
                            : "Requires Premium Player discovery access.";

                return new ActivityOptionState(
                    definition,
                    isAvailable,
                    unavailableReason,
                    selectedActivityTypes?.Contains(definition.Code) == true);
            })
            .ToArray();
    }

    private static bool HasRequiredFeatures(
        ActivityOptionDefinition definition,
        IReadOnlyCollection<string> featureCodes)
    {
        if (definition.RequiredFeatureCodes.Count == 0)
        {
            return true;
        }

        return definition.RequireAnyFeature
            ? definition.RequiredFeatureCodes.Any(feature => featureCodes.Contains(feature, StringComparer.Ordinal))
            : definition.RequiredFeatureCodes.All(feature => featureCodes.Contains(feature, StringComparer.Ordinal));
    }

    private async Task<UserDashboardPreference?> GetPreferenceAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.UserDashboardPreferences
            .SingleOrDefaultAsync(preference => preference.UserId == userId, cancellationToken);
    }

    private static IReadOnlyCollection<string> NormalizeActivityTypes(IReadOnlyCollection<string>? activityTypes)
    {
        if (activityTypes is null || activityTypes.Count == 0)
        {
            return [];
        }

        var supportedCodes = DashboardActivityTypeCodes.All.ToHashSet(StringComparer.Ordinal);
        return activityTypes
            .Select(activityType => activityType?.Trim())
            .Where(activityType => !string.IsNullOrWhiteSpace(activityType))
            .Cast<string>()
            .Select(activityType => activityType.Replace("-", "_").Replace(" ", "_").ToLowerInvariant())
            .Select(activityType => activityType == "team_new_players"
                ? DashboardActivityTypeCodes.TeamNewListings
                : activityType)
            .Where(supportedCodes.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(activityType => activityType, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ParseActivityTypes(string? activityTypesJson)
    {
        if (string.IsNullOrWhiteSpace(activityTypesJson))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(activityTypesJson);
            return NormalizeActivityTypes(parsed ?? []);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ResolveOpportunityActivityType(string type)
    {
        var normalized = type.ToLowerInvariant();
        if (normalized.Contains("tournament", StringComparison.Ordinal))
        {
            return DashboardActivityTypeCodes.Tournaments;
        }

        if (normalized.Contains("pickup", StringComparison.Ordinal))
        {
            return DashboardActivityTypeCodes.PickupOpportunities;
        }

        if (normalized.Contains("roster", StringComparison.Ordinal)
            || normalized.Contains("opening", StringComparison.Ordinal))
        {
            return DashboardActivityTypeCodes.RosterOpenings;
        }

        if (normalized.Contains("camp", StringComparison.Ordinal)
            || normalized.Contains("clinic", StringComparison.Ordinal)
            || normalized.Contains("workout", StringComparison.Ordinal))
        {
            return DashboardActivityTypeCodes.CampsAndClinics;
        }

        return DashboardActivityTypeCodes.Tryouts;
    }

    private static string ResolvePlayerListingActivityType(
        string listingType,
        bool includeTeamRelevantListings)
    {
        if (string.Equals(listingType, TryOutSpotPlayerListingTypes.UsedEquipment, StringComparison.Ordinal))
        {
            return DashboardActivityTypeCodes.ForSaleItems;
        }

        return includeTeamRelevantListings
            ? DashboardActivityTypeCodes.TeamNewListings
            : DashboardActivityTypeCodes.PickupOpportunities;
    }

    private static string GetActivityTypeLabel(string activityType)
    {
        return activityType switch
        {
            DashboardActivityTypeCodes.Tryouts => "Tryout",
            DashboardActivityTypeCodes.Tournaments => "Tournament",
            DashboardActivityTypeCodes.PickupOpportunities => "Pickup",
            DashboardActivityTypeCodes.ForSaleItems => "For sale",
            DashboardActivityTypeCodes.RosterOpenings => "Roster opening",
            DashboardActivityTypeCodes.CampsAndClinics => "Camp or clinic",
            DashboardActivityTypeCodes.TeamNewListings => "Player listing",
            _ => "Activity"
        };
    }

    private static bool ContainsAny(IReadOnlySet<string> values, params string[] candidates)
    {
        return candidates.Any(values.Contains);
    }

    private static bool IsPlayerParentRole(string role)
    {
        return string.Equals(role, TryOutSpotRoles.Parent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, TryOutSpotRoles.Player, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FormatLocation(string? city, string? state)
    {
        var normalizedCity = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        var normalizedState = string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant();
        return (normalizedCity, normalizedState) switch
        {
            (null, null) => null,
            (not null, null) => normalizedCity,
            (null, not null) => normalizedState,
            _ => $"{normalizedCity}, {normalizedState}"
        };
    }

    private static DateTime MaxDate(DateTime? first, DateTime second, DateTime third)
    {
        var max = first.HasValue && first.Value > second ? first.Value : second;
        return max > third ? max : third;
    }

    private sealed record ActivityOptionDefinition(
        string Code,
        string Label,
        string Description,
        string Audience,
        bool AppliesToPlayerParent,
        bool AppliesToTeam,
        IReadOnlyCollection<string> RequiredFeatureCodes,
        bool IsDefaultSelected,
        bool RequireAnyFeature = false);

    private sealed record ActivityOptionState(
        ActivityOptionDefinition Definition,
        bool IsAvailable,
        string? UnavailableReason,
        bool IsSelected);

    private sealed record DashboardActivitySectionSource(
        string Code,
        string Title,
        string EmptyMessage,
        int TotalCount,
        Func<int, CancellationToken, Task<IReadOnlyCollection<DashboardActivityItemResponse>>> LoadItemsAsync);

    private sealed record DashboardActivityPagedItem(
        string SectionCode,
        DashboardActivityItemResponse Item);
}
