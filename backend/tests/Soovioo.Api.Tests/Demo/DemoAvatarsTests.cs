using SkiaSharp;
using Soovioo.Api.Demo;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Demo;

public class DemoAvatarsTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void EveryVariantComesInBothSizes(bool female, bool adult)
    {
        for (var variant = 0; variant < DemoAvatars.VariantsPerKind; variant++)
        {
            var files = DemoAvatars.Get(female, adult, variant);
            using var thumb = SKBitmap.Decode(files[PhotoSize.Thumb]);
            using var profile = SKBitmap.Decode(files[PhotoSize.Profile]);
            Assert.Equal(128, thumb.Width);
            Assert.Equal(512, profile.Width);
        }
    }

    [Fact]
    public void VariantsWrapAround() =>
        Assert.Same(DemoAvatars.Get(true, false, 3), DemoAvatars.Get(true, false, 3 + DemoAvatars.VariantsPerKind));
}
