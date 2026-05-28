using System.Net;

namespace TryOutSpot.Web.Tests;

public sealed class HomePageSeoTests
{
    [Fact]
    public async Task HomePage_RendersSearchAndSocialMetadata()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Contains("<title>TryOutSpot | Baseball and Softball Tryouts, Teams, and Player Profiles</title>", html);
        Assert.Contains(
            "<meta name=\"description\" content=\"Find baseball and softball tryouts, roster openings, team opportunities, and player profiles with TryOutSpot. Connect players, parents, coaches, and organizations in one trusted platform.\" />",
            html);
        Assert.Contains("<link rel=\"canonical\" href=\"http://localhost/\" />", html);
        Assert.Contains("<meta property=\"og:site_name\" content=\"TryOutSpot\" />", html);
        Assert.Contains("<meta property=\"og:title\" content=\"TryOutSpot | Baseball and Softball Tryouts, Teams, and Player Profiles\" />", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\" />", html);
        Assert.Contains("\"@type\":\"WebSite\"", html);
        Assert.Contains("\"@type\":\"Organization\"", html);
        Assert.Contains("<h1>Baseball and Softball Tryouts, Teams, and Player Profiles</h1>", decodedHtml);
        Assert.DoesNotContain("Home Page - TryOutSpot", html);
    }

    [Fact]
    public async Task PlansAndFeatures_RendersTeamSharingAndFollowerSmsRules()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/plans-and-features");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Contains("Share listing links", decodedHtml);
        Assert.Contains("opportunities.share.links", decodedHtml);
        Assert.Contains("Follower SMS updates", decodedHtml);
        Assert.Contains("communication.sms.followers", decodedHtml);
        Assert.Contains("who follow an opportunity", decodedHtml);
    }
}
