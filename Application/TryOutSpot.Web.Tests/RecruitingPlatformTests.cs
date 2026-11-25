using TryOutSpot.Web.Listings;

namespace TryOutSpot.Web.Tests;

public sealed class RecruitingPlatformTests
{
    [Theory]
    [InlineData("https://www.fieldlevel.com/app/profile/brock.thompson.2172819/baseball", "fieldlevel")]
    [InlineData("https://recruit-match.ncsasports.org/clientrms/athlete_profile/123", "ncsa")]
    [InlineData("https://www.perfectgame.org/Players/Playerprofile.aspx?ID=1", "perfectgame")]
    [InlineData("https://www.prepbaseballreport.com/profiles/KS/Player-1", "prepbaseball")]
    [InlineData("https://web.gc.com/teams/abc", "gamechanger")]
    [InlineData("https://www.hudl.com/profile/123", "hudl")]
    public void Detect_RecognizesKnownPlatforms(string url, string expectedCode)
    {
        Assert.Equal(expectedCode, TryOutSpotRecruitingPlatforms.Detect(new Uri(url))?.Code);
    }

    [Fact]
    public void Detect_DoesNotMatchLookalikeDomains()
    {
        Assert.Null(TryOutSpotRecruitingPlatforms.Detect(new Uri("https://notfieldlevel.com/profile")));
    }

    [Theory]
    [InlineData("fieldlevel", "https://www.fieldlevel.com/x", "FieldLevel")]
    [InlineData("fieldlevel_2", "https://www.fieldlevel.com/y", "FieldLevel")]
    [InlineData("other", "https://www.fivetoolbaseball.com/player/1", "fivetoolbaseball.com")]
    [InlineData("other_3", "https://recruits.example.org/p", "recruits.example.org")]
    public void GetLabel_UsesPlatformNameOrHost(string key, string url, string expectedLabel)
    {
        Assert.Equal(expectedLabel, TryOutSpotRecruitingPlatforms.GetLabel(key, url));
    }
}
