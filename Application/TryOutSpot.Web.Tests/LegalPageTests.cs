using System.Net;

namespace TryOutSpot.Web.Tests;

public sealed class LegalPageTests
{
    [Theory]
    [InlineData("/privacy-policy", "No mobile information will be shared")]
    [InlineData("/terms-and-conditions", "Reply STOP to opt out")]
    [InlineData("/sms-consent", "TryOutSpot SMS Consent Flow")]
    public async Task LegalPages_RenderPublicly(string path, string expectedContent)
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedContent, content);
    }
}
