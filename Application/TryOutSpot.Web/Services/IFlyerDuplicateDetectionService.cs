namespace TryOutSpot.Web.Services;

public interface IFlyerDuplicateDetectionService
{
    int BlockingProbabilityPercent { get; }

    int CautionProbabilityPercent { get; }

    Task<FlyerDuplicateCheckResult> FindDuplicatesAsync(
        Guid flyerImportId,
        CancellationToken cancellationToken);
}

public sealed record FlyerDuplicateCheckResult(
    int BlockingProbabilityPercent,
    int CautionProbabilityPercent,
    IReadOnlyCollection<FlyerDuplicateCandidate> Candidates)
{
    public FlyerDuplicateCandidate? TopCandidate => Candidates
        .OrderByDescending(candidate => candidate.ProbabilityPercent)
        .FirstOrDefault();

    public bool HasBlockingDuplicate => TopCandidate?.ProbabilityPercent >= BlockingProbabilityPercent;

    public bool HasCautionDuplicate => TopCandidate?.ProbabilityPercent >= CautionProbabilityPercent;
}

public sealed record FlyerDuplicateCandidate(
    int ProbabilityPercent,
    string CandidateType,
    Guid? FlyerImportId,
    Guid? OpportunityId,
    Guid? TeamId,
    string? Status,
    string? Title,
    string? TeamName,
    string? OrganizationName,
    string? SportName,
    string? OpportunityType,
    string? AgeGroup,
    DateTime? EventDateUtc,
    string? Location,
    string? City,
    string? State,
    string? ZipCode,
    string? ContactEmail,
    string? ContactPhone,
    string? SourceUrl,
    IReadOnlyCollection<string> MatchedSignals);
