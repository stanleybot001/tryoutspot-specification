namespace TryOutSpot.Web.Listings;

public sealed record OpportunityRegistrationFieldDefinition(
    string Code,
    string Label,
    string Description);

public static class TryOutSpotOpportunityRegistrationFields
{
    public const string PlayerName = "player_name";
    public const string PlayerBirthDate = "player_birthdate";
    public const string PlayerSchool = "player_school";
    public const string PlayerPhone = "player_phone";
    public const string PlayerEmail = "player_email";
    public const string GuardianName = "guardian_name";
    public const string GuardianEmail = "guardian_email";
    public const string GuardianPhone = "guardian_phone";
    public const string EmergencyContactName = "emergency_contact_name";
    public const string EmergencyContactPhone = "emergency_contact_phone";
    public const string MedicalInfo = "medical_info";
    public const string WaiverSignature = "waiver_signature";
    public const string AdditionalNotes = "additional_notes";

    public static readonly OpportunityRegistrationFieldDefinition[] All =
    [
        new(
            PlayerName,
            "Player name",
            "Capture the player name from the selected player profile."),
        new(
            PlayerBirthDate,
            "Player birthdate",
            "Capture the player birthdate from the selected player profile."),
        new(
            PlayerSchool,
            "Player school",
            "Capture the player school name from profile or registration form."),
        new(
            PlayerPhone,
            "Player cell phone",
            "Capture the player cell phone from profile or registration form."),
        new(
            PlayerEmail,
            "Player email address",
            "Capture the player email from profile or registration form."),
        new(
            GuardianName,
            "Guardian name",
            "Parent or guardian name for registration confirmation."),
        new(
            GuardianEmail,
            "Guardian email",
            "Parent or guardian email for registration updates."),
        new(
            GuardianPhone,
            "Guardian phone",
            "Parent or guardian phone number for day-of communication."),
        new(
            EmergencyContactName,
            "Emergency contact name",
            "Emergency contact full name."),
        new(
            EmergencyContactPhone,
            "Emergency contact phone",
            "Emergency contact phone number."),
        new(
            MedicalInfo,
            "Medical notes",
            "Allergies, medical alerts, or relevant health notes."),
        new(
            WaiverSignature,
            "Waiver acknowledgment",
            "Require a waiver acknowledgment before submitting registration."),
        new(
            AdditionalNotes,
            "Additional notes",
            "Open text field for team-specific notes from the registrant.")
    ];

    public static IReadOnlyCollection<string> NormalizeSelectedCodes(IEnumerable<string>? selectedCodes)
    {
        if (selectedCodes is null)
        {
            return [];
        }

        var knownCodes = All
            .Select(field => field.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = selectedCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(code => knownCodes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized;
    }

    public static bool IsKnownCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        return All.Any(field => string.Equals(field.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
