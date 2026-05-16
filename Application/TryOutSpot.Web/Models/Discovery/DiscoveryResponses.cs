namespace TryOutSpot.Web.Models.Discovery;

/// <summary>
/// Paginated team directory search response.
/// </summary>
public sealed record TeamDiscoveryListResponse(
    IReadOnlyCollection<TeamDiscoverySummaryResponse> Teams,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    string? SearchOriginZipCode = null,
    int? SearchRadiusMiles = null);

/// <summary>
/// Public team listing summary.
/// </summary>
public sealed record TeamDiscoverySummaryResponse(
    Guid Id,
    string Name,
    Guid? OrganizationId,
    string? OrganizationName,
    string? LogoImageUrl,
    string? TeamLevel,
    string GeographicScope,
    IReadOnlyCollection<string> Sports,
    string? City,
    string? State,
    string? ZipCode,
    bool IsVerified,
    bool IsContactInfoVisible,
    string? WebsiteUrl,
    string? ContactEmail,
    string? ContactPhone,
    string? SocialMediaLinks,
    double? DistanceMiles = null);

/// <summary>
/// Paginated organization directory search response.
/// </summary>
public sealed record OrganizationDiscoveryListResponse(
    IReadOnlyCollection<OrganizationDiscoverySummaryResponse> Organizations,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    string? SearchOriginZipCode = null,
    int? SearchRadiusMiles = null);

/// <summary>
/// Public organization listing summary.
/// </summary>
public sealed record OrganizationDiscoverySummaryResponse(
    Guid Id,
    string Name,
    bool IsAcademy,
    IReadOnlyCollection<string> Sports,
    int TeamCount,
    string? City,
    string? State,
    string? ZipCode,
    bool IsVerified,
    bool IsContactInfoVisible,
    string? WebsiteUrl,
    string? ContactEmail,
    string? ContactPhone,
    string? SocialMediaLinks,
    double? DistanceMiles = null);

/// <summary>
/// Paginated opportunity search response.
/// </summary>
public sealed record OpportunityDiscoveryListResponse(
    IReadOnlyCollection<OpportunityDiscoverySummaryResponse> Opportunities,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    string? SearchOriginZipCode = null,
    int? SearchRadiusMiles = null);

/// <summary>
/// Public opportunity listing summary.
/// </summary>
public sealed record OpportunityDiscoverySummaryResponse(
    Guid Id,
    Guid TeamId,
    string TeamName,
    string? TeamLogoImageUrl,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid SportId,
    string SportName,
    string Type,
    string Title,
    string? Description,
    string? CompetitionLevel,
    string? AgeGroup,
    decimal RegistrationFee,
    DateTime? RegistrationDeadline,
    DateTime? EventDate,
    DateTime? EventEndDate,
    string? City,
    string? State,
    string? ZipCode,
    bool IsContactInfoVisible,
    string? ContactEmail,
    string? ContactPhone,
    string? WebsiteUrl,
    string? PdfUrl,
    double? DistanceMiles = null);
