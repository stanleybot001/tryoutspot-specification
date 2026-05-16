namespace TryOutSpot.Web.Services;

public sealed class R2StorageOptions
{
    public const string SectionName = "R2Storage";

    public string AccountId { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;
}
