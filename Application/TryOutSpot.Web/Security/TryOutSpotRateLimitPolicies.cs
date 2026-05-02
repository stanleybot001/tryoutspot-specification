namespace TryOutSpot.Web.Security;

public static class TryOutSpotRateLimitPolicies
{
    public const string AccountSecurity = "account-security";

    public static string GetPartitionKey(HttpContext httpContext)
    {
        var remoteAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"{remoteAddress}:{httpContext.Request.Path}";
    }
}
