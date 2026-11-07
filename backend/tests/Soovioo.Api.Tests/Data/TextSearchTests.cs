using Soovioo.Api.Data;

namespace Soovioo.Api.Tests.Data;

public class TextSearchTests
{
    [Theory]
    [InlineData("Plzeň", "plzen")]
    [InlineData("ŽLUŤOUČKÝ KŮŇ", "zlutoucky kun")]
    [InlineData("Šalda", "salda")]
    [InlineData("Ústí nad Orlicí", "usti nad orlici")]
    [InlineData("abc-123", "abc-123")]
    public void FoldIgnoresCaseAndDiacritics(string text, string folded) =>
        Assert.Equal(folded, TextSearch.Fold(text));
}
