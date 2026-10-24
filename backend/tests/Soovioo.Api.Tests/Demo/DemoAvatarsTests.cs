using SkiaSharp;
using Soovioo.Api.Demo;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Demo;

public class DemoAvatarsTests
{
    [Theory]
    [InlineData(DemoAvatarSet.Boys)]
    [InlineData(DemoAvatarSet.Girls)]
    [InlineData(DemoAvatarSet.Men)]
    [InlineData(DemoAvatarSet.Women)]
    public void EveryPortraitOfEverySetComesInBothSizes(DemoAvatarSet set)
    {
        for (var variant = 0; variant < DemoAvatars.VariantsPerSet; variant++)
        {
            var files = DemoAvatars.Get(set, variant);
            using var thumb = SKBitmap.Decode(files[PhotoSize.Thumb]);
            using var profile = SKBitmap.Decode(files[PhotoSize.Profile]);
            Assert.Equal(128, thumb.Width);
            Assert.Equal(512, profile.Width);
        }
    }

    [Fact]
    public void PortraitsWrapAround() =>
        Assert.Same(DemoAvatars.Get(DemoAvatarSet.Girls, 3), DemoAvatars.Get(DemoAvatarSet.Girls, 3 + DemoAvatars.VariantsPerSet));

    [Theory]
    [InlineData(false, false, DemoAvatarSet.Boys)]
    [InlineData(true, false, DemoAvatarSet.Girls)]
    [InlineData(false, true, DemoAvatarSet.Men)]
    [InlineData(true, true, DemoAvatarSet.Women)]
    public void SetFollowsGenderAndAge(bool female, bool adult, DemoAvatarSet expected) =>
        Assert.Equal(expected, DemoAvatars.SetFor(female, adult));
}
