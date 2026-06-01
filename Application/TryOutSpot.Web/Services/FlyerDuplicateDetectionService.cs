using System.Text;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Listings;

namespace TryOutSpot.Web.Services;

public sealed class FlyerDuplicateDetectionService(AppDbContext dbContext) : IFlyerDuplicateDetectionService
{
    private const int CandidateLimit = 200;
    private const int ResultLimit = 5;

    public int BlockingProbabilityPercent => 85;

    public int CautionProbabilityPercent => 70;

    public async Task<FlyerDuplicateCheckResult> FindDuplicatesAsync(
        Guid flyerImportId,
        CancellationToken cancellationToken)
    {
        var flyerImport = await dbContext.FlyerImports
            .AsNoTracking()
            .Include(currentImport => currentImport.Sport)
            .SingleOrDefaultAsync(currentImport => currentImport.Id == flyerImportId, cancellationToken);
        if (flyerImport is null)
        {
            return EmptyResult();
        }

        var current = DuplicateReference.FromFlyerImport(flyerImport);
        var candidates = new List<FlyerDuplicateCandidate>();
        candidates.AddRange(await FindFlyerImportCandidatesAsync(current, flyerImportId, cancellationToken));
        candidates.AddRange(await FindOpportunityCandidatesAsync(current, flyerImport.OpportunityId, cancellationToken));

        var topCandidates = candidates
            .Where(candidate => candidate.ProbabilityPercent >= CautionProbabilityPercent)
            .GroupBy(BuildCandidateKey)
            .Select(group => group.OrderByDescending(candidate => candidate.ProbabilityPercent).First())
            .OrderByDescending(candidate => candidate.ProbabilityPercent)
            .ThenBy(candidate => candidate.Title)
            .Take(ResultLimit)
            .ToArray();

        return new FlyerDuplicateCheckResult(
            BlockingProbabilityPercent,
            CautionProbabilityPercent,
            topCandidates);
    }

    private async Task<IReadOnlyCollection<FlyerDuplicateCandidate>> FindFlyerImportCandidatesAsync(
        DuplicateReference current,
        Guid flyerImportId,
        CancellationToken cancellationToken)
    {
        var contentHash = NormalizeOptional(current.ContentHash);
        var sourceUrl = NormalizeUrl(current.SourceUrl);
        var externalImageUrl = NormalizeUrl(current.OriginalExternalImageUrl);
        var email = NormalizeEmail(current.ContactEmail);
        var phoneTail = NormalizePhoneTail(current.ContactPhone);
        var titleNeedle = BuildSearchNeedle(current.Title);
        var teamNeedle = BuildSearchNeedle(current.TeamName);
        var zipCode = NormalizeCompact(current.ZipCode);
        var eventFrom = current.EventDateUtc?.Date.AddDays(-45);
        var eventTo = current.EventDateUtc?.Date.AddDays(45);

        var flyerImports = await dbContext.FlyerImports
            .AsNoTracking()
            .Include(candidate => candidate.Sport)
            .Where(candidate => candidate.Id != flyerImportId
                && candidate.Status != TryOutSpotFlyerImportStatuses.Rejected
                && ((contentHash != null && candidate.ContentHash == contentHash)
                    || (sourceUrl != null
                        && candidate.SourceUrl != null
                        && candidate.SourceUrl.ToLower() == sourceUrl)
                    || (externalImageUrl != null
                        && candidate.OriginalExternalImageUrl != null
                        && candidate.OriginalExternalImageUrl.ToLower() == externalImageUrl)
                    || (email != null
                        && candidate.ContactEmail != null
                        && candidate.ContactEmail.ToLower() == email)
                    || (phoneTail != null
                        && candidate.ContactPhone != null
                        && candidate.ContactPhone.Contains(phoneTail))
                    || (titleNeedle != null
                        && candidate.Title != null
                        && candidate.Title.ToLower().Contains(titleNeedle))
                    || (teamNeedle != null
                        && candidate.TeamName != null
                        && candidate.TeamName.ToLower().Contains(teamNeedle))
                    || (zipCode != null
                        && candidate.ZipCode != null
                        && candidate.ZipCode == zipCode)
                    || (eventFrom.HasValue
                        && eventTo.HasValue
                        && candidate.EventDate.HasValue
                        && candidate.EventDate.Value >= eventFrom.Value
                        && candidate.EventDate.Value <= eventTo.Value)))
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .Take(CandidateLimit)
            .ToArrayAsync(cancellationToken);

        return flyerImports
            .Select(candidate => BuildCandidate(current, DuplicateReference.FromFlyerImport(candidate)))
            .Where(candidate => candidate is not null)
            .Cast<FlyerDuplicateCandidate>()
            .ToArray();
    }

    private async Task<IReadOnlyCollection<FlyerDuplicateCandidate>> FindOpportunityCandidatesAsync(
        DuplicateReference current,
        Guid? currentOpportunityId,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(current.ContactEmail);
        var phoneTail = NormalizePhoneTail(current.ContactPhone);
        var titleNeedle = BuildSearchNeedle(current.Title);
        var teamNeedle = BuildSearchNeedle(current.TeamName);
        var zipCode = NormalizeCompact(current.ZipCode);
        var eventFrom = current.EventDateUtc?.Date.AddDays(-45);
        var eventTo = current.EventDateUtc?.Date.AddDays(45);

        var opportunities = await dbContext.Opportunities
            .AsNoTracking()
            .Include(candidate => candidate.Sport)
            .Include(candidate => candidate.Team)
                .ThenInclude(team => team.Organization)
            .Where(candidate => candidate.IsActive
                && (!currentOpportunityId.HasValue || candidate.Id != currentOpportunityId.Value)
                && ((email != null
                        && ((candidate.ContactEmail != null && candidate.ContactEmail.ToLower() == email)
                            || (candidate.Team.Email != null && candidate.Team.Email.ToLower() == email)))
                    || (phoneTail != null
                        && ((candidate.ContactPhone != null && candidate.ContactPhone.Contains(phoneTail))
                            || (candidate.Team.PhoneNumber != null && candidate.Team.PhoneNumber.Contains(phoneTail))))
                    || (titleNeedle != null
                        && candidate.Title.ToLower().Contains(titleNeedle))
                    || (teamNeedle != null
                        && candidate.Team.Name.ToLower().Contains(teamNeedle))
                    || (zipCode != null
                        && candidate.ZipCode != null
                        && candidate.ZipCode == zipCode)
                    || (eventFrom.HasValue
                        && eventTo.HasValue
                        && candidate.EventDate.HasValue
                        && candidate.EventDate.Value >= eventFrom.Value
                        && candidate.EventDate.Value <= eventTo.Value)))
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .Take(CandidateLimit)
            .ToArrayAsync(cancellationToken);

        return opportunities
            .Select(candidate => BuildCandidate(current, DuplicateReference.FromOpportunity(candidate)))
            .Where(candidate => candidate is not null)
            .Cast<FlyerDuplicateCandidate>()
            .ToArray();
    }

    private FlyerDuplicateCandidate? BuildCandidate(DuplicateReference current, DuplicateReference candidate)
    {
        var matchedSignals = new List<string>();
        var score = ScoreCandidate(current, candidate, matchedSignals);
        if (score < CautionProbabilityPercent)
        {
            return null;
        }

        return new FlyerDuplicateCandidate(
            score,
            candidate.CandidateType,
            candidate.FlyerImportId,
            candidate.OpportunityId,
            candidate.TeamId,
            candidate.Status,
            candidate.Title,
            candidate.TeamName,
            candidate.OrganizationName,
            candidate.SportName,
            candidate.OpportunityType,
            candidate.AgeGroup,
            candidate.EventDateUtc,
            BuildLocationSummary(candidate),
            candidate.City,
            candidate.State,
            candidate.ZipCode,
            candidate.ContactEmail,
            candidate.ContactPhone,
            candidate.SourceUrl,
            matchedSignals);
    }

    private static int ScoreCandidate(
        DuplicateReference current,
        DuplicateReference candidate,
        ICollection<string> matchedSignals)
    {
        var score = 0;

        if (SameOptional(current.ContentHash, candidate.ContentHash))
        {
            score = Math.Max(score, 100);
            matchedSignals.Add("same uploaded flyer image");
        }
        else if (SameUrl(current.SourceUrl, candidate.SourceUrl))
        {
            score = Math.Max(score, 96);
            matchedSignals.Add("same source post URL");
        }
        else if (SameUrl(current.OriginalExternalImageUrl, candidate.OriginalExternalImageUrl))
        {
            score = Math.Max(score, 92);
            matchedSignals.Add("same external image URL");
        }

        score += ScoreContact(current, candidate, matchedSignals);
        score += ScoreText(current.TeamName, candidate.TeamName, "team name", 16, 8, matchedSignals);
        score += ScoreText(current.Title, candidate.Title, "listing title", 18, 10, matchedSignals);
        score += ScoreDate(current.EventDateUtc, candidate.EventDateUtc, matchedSignals);
        score += ScoreExactField(current.AgeGroup, candidate.AgeGroup, "age group", 8, matchedSignals);
        score += ScoreExactField(current.SportName, candidate.SportName, "sport", 6, matchedSignals);
        score += ScoreExactField(current.OpportunityType, candidate.OpportunityType, "listing type", 6, matchedSignals);
        score += ScoreLocation(current, candidate, matchedSignals);

        return Math.Min(score, 100);
    }

    private static int ScoreContact(
        DuplicateReference current,
        DuplicateReference candidate,
        ICollection<string> matchedSignals)
    {
        var sameEmail = SameEmail(current.ContactEmail, candidate.ContactEmail);
        var samePhone = SamePhone(current.ContactPhone, candidate.ContactPhone);
        if (sameEmail && samePhone)
        {
            matchedSignals.Add("same contact email and phone");
            return 32;
        }

        if (sameEmail)
        {
            matchedSignals.Add("same contact email");
            return 22;
        }

        if (samePhone)
        {
            matchedSignals.Add("same contact phone");
            return 22;
        }

        return 0;
    }

    private static int ScoreText(
        string? current,
        string? candidate,
        string label,
        int highScore,
        int mediumScore,
        ICollection<string> matchedSignals)
    {
        var similarity = TextSimilarity(current, candidate);
        if (similarity >= 90)
        {
            matchedSignals.Add($"same {label}");
            return highScore;
        }

        if (similarity >= 75)
        {
            matchedSignals.Add($"similar {label}");
            return mediumScore;
        }

        return 0;
    }

    private static int ScoreDate(
        DateTime? current,
        DateTime? candidate,
        ICollection<string> matchedSignals)
    {
        if (!current.HasValue || !candidate.HasValue)
        {
            return 0;
        }

        var daysApart = Math.Abs((current.Value.Date - candidate.Value.Date).TotalDays);
        if (daysApart == 0)
        {
            matchedSignals.Add("same event date");
            return 22;
        }

        if (daysApart <= 1)
        {
            matchedSignals.Add("event dates are within one day");
            return 14;
        }

        if (daysApart <= 7)
        {
            matchedSignals.Add("event dates are within one week");
            return 8;
        }

        if (daysApart <= 30)
        {
            matchedSignals.Add("event dates are close");
            return 3;
        }

        return 0;
    }

    private static int ScoreExactField(
        string? current,
        string? candidate,
        string label,
        int points,
        ICollection<string> matchedSignals)
    {
        if (!SameCompact(current, candidate))
        {
            return 0;
        }

        matchedSignals.Add($"same {label}");
        return points;
    }

    private static int ScoreLocation(
        DuplicateReference current,
        DuplicateReference candidate,
        ICollection<string> matchedSignals)
    {
        var score = 0;
        if (SameCompact(current.Address, candidate.Address))
        {
            score += 10;
            matchedSignals.Add("same address");
        }
        else
        {
            var locationSimilarity = TextSimilarity(current.Location, candidate.Location);
            if (locationSimilarity >= 85)
            {
                score += 8;
                matchedSignals.Add("same location name");
            }
            else if (locationSimilarity >= 75)
            {
                score += 4;
                matchedSignals.Add("similar location name");
            }
        }

        if (SameCompact(current.ZipCode, candidate.ZipCode))
        {
            score += 6;
            matchedSignals.Add("same ZIP code");
        }

        if (SameCompact(current.City, candidate.City) && SameCompact(current.State, candidate.State))
        {
            score += 4;
            matchedSignals.Add("same city and state");
        }

        return score;
    }

    private FlyerDuplicateCheckResult EmptyResult()
    {
        return new FlyerDuplicateCheckResult(
            BlockingProbabilityPercent,
            CautionProbabilityPercent,
            []);
    }

    private static string BuildCandidateKey(FlyerDuplicateCandidate candidate)
    {
        if (candidate.OpportunityId.HasValue)
        {
            return $"opportunity:{candidate.OpportunityId.Value}";
        }

        if (candidate.FlyerImportId.HasValue)
        {
            return $"flyer:{candidate.FlyerImportId.Value}";
        }

        return $"{candidate.CandidateType}:{candidate.Title}:{candidate.EventDateUtc:o}";
    }

    private static string? BuildLocationSummary(DuplicateReference candidate)
    {
        var locationParts = new[]
        {
            candidate.Location,
            candidate.Address,
            BuildCityStateZip(candidate.City, candidate.State, candidate.ZipCode)
        };

        var location = string.Join(", ", locationParts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(location) ? null : location;
    }

    private static string? BuildCityStateZip(string? city, string? state, string? zipCode)
    {
        var cityState = string.Join(", ", new[] { city, state }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.Join(" ", new[] { cityState, zipCode }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string? BuildSearchNeedle(string? value)
    {
        var normalized = NormalizeOptional(value)?.ToLowerInvariant();
        if (normalized is null || normalized.Length < 4)
        {
            return null;
        }

        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static bool SameEmail(string? left, string? right)
    {
        var normalizedLeft = NormalizeEmail(left);
        var normalizedRight = NormalizeEmail(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static bool SamePhone(string? left, string? right)
    {
        var normalizedLeft = NormalizePhone(left);
        var normalizedRight = NormalizePhone(right);
        if (normalizedLeft.Length < 7 || normalizedRight.Length < 7)
        {
            return false;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return true;
        }

        return normalizedLeft.Length >= 10
            && normalizedRight.Length >= 10
            && string.Equals(normalizedLeft[^10..], normalizedRight[^10..], StringComparison.Ordinal);
    }

    private static bool SameUrl(string? left, string? right)
    {
        var normalizedLeft = NormalizeUrl(left);
        var normalizedRight = NormalizeUrl(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static bool SameOptional(string? left, string? right)
    {
        var normalizedLeft = NormalizeOptional(left);
        var normalizedRight = NormalizeOptional(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static bool SameCompact(string? left, string? right)
    {
        var normalizedLeft = NormalizeCompact(left);
        var normalizedRight = NormalizeCompact(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static string? NormalizeEmail(string? value)
    {
        return NormalizeOptional(value)?.ToLowerInvariant();
    }

    private static string? NormalizeUrl(string? value)
    {
        return NormalizeOptional(value)?.TrimEnd('/').ToLowerInvariant();
    }

    private static string? NormalizePhoneTail(string? value)
    {
        var phone = NormalizePhone(value);
        return phone.Length >= 4 ? phone[^4..] : null;
    }

    private static string NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = string.Concat(value.Where(char.IsDigit));
        return digits.Length == 11 && digits.StartsWith('1') ? digits[1..] : digits;
    }

    private static string? NormalizeCompact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int TextSimilarity(string? left, string? right)
    {
        var normalizedLeft = NormalizeComparable(left);
        var normalizedRight = NormalizeComparable(right);
        if (normalizedLeft is null || normalizedRight is null)
        {
            return 0;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return 100;
        }

        var shorter = normalizedLeft.Length <= normalizedRight.Length ? normalizedLeft : normalizedRight;
        var longer = normalizedLeft.Length <= normalizedRight.Length ? normalizedRight : normalizedLeft;
        if (shorter.Length >= 4 && longer.Contains(shorter, StringComparison.Ordinal))
        {
            return 85;
        }

        var leftTokens = normalizedLeft.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = normalizedRight.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var sharedTokenCount = leftTokens.Intersect(rightTokens).Count();
        if (sharedTokenCount == 0)
        {
            return 0;
        }

        var diceScore = (int)Math.Round(200d * sharedTokenCount / (leftTokens.Count + rightTokens.Count));
        var overlapScore = sharedTokenCount >= 2
            ? (int)Math.Round(100d * sharedTokenCount / Math.Min(leftTokens.Count, rightTokens.Count))
            : 0;
        if (overlapScore >= 75)
        {
            return Math.Max(diceScore, 82);
        }

        if (overlapScore >= 50)
        {
            return Math.Max(diceScore, 72);
        }

        return diceScore;
    }

    private static string? NormalizeComparable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = true;
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.Length == 0
            ? null
            : builder.ToString().Trim();
    }

    private sealed record DuplicateReference(
        string CandidateType,
        Guid? FlyerImportId,
        Guid? OpportunityId,
        Guid? TeamId,
        string? Status,
        string? SourceUrl,
        string? OriginalExternalImageUrl,
        string? ContentHash,
        string? Title,
        string? TeamName,
        string? OrganizationName,
        string? SportName,
        string? OpportunityType,
        string? AgeGroup,
        DateTime? EventDateUtc,
        string? Location,
        string? Address,
        string? City,
        string? State,
        string? ZipCode,
        string? ContactEmail,
        string? ContactPhone)
    {
        public static DuplicateReference FromFlyerImport(FlyerImport flyerImport)
        {
            return new DuplicateReference(
                "Flyer import",
                flyerImport.Id,
                flyerImport.OpportunityId,
                flyerImport.TeamId,
                flyerImport.Status,
                flyerImport.SourceUrl,
                flyerImport.OriginalExternalImageUrl,
                flyerImport.ContentHash,
                flyerImport.Title,
                flyerImport.TeamName,
                flyerImport.OrganizationName,
                flyerImport.Sport?.Name ?? flyerImport.SportName,
                flyerImport.OpportunityType,
                flyerImport.AgeGroup,
                flyerImport.EventDate,
                flyerImport.Location,
                flyerImport.Address,
                flyerImport.City,
                flyerImport.State,
                flyerImport.ZipCode,
                flyerImport.ContactEmail,
                flyerImport.ContactPhone);
        }

        public static DuplicateReference FromOpportunity(Opportunity opportunity)
        {
            return new DuplicateReference(
                "Team opportunity",
                FlyerImportId: null,
                opportunity.Id,
                opportunity.TeamId,
                opportunity.IsPublished ? "published" : "draft",
                SourceUrl: null,
                OriginalExternalImageUrl: null,
                ContentHash: null,
                opportunity.Title,
                opportunity.Team.Name,
                opportunity.Team.Organization?.Name,
                opportunity.Sport.Name,
                opportunity.Type,
                opportunity.AgeGroup,
                opportunity.EventDate,
                opportunity.Location,
                opportunity.Address,
                opportunity.City,
                opportunity.State,
                opportunity.ZipCode,
                opportunity.ContactEmail ?? opportunity.Team.Email,
                opportunity.ContactPhone ?? opportunity.Team.PhoneNumber);
        }
    }
}
