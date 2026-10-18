using Soovioo.Api.Evidence;

namespace Soovioo.Api.Tests.Evidence;

public class StudentTermTests
{
    [Theory]
    [InlineData("Žák: Jan Novák", SchoolType.Secondary, "Student: Jan Novák")]
    [InlineData("Všichni žáci", SchoolType.Secondary, "Všichni studenti")]
    [InlineData("Hledat žáka", SchoolType.Secondary, "Hledat studenta")]
    [InlineData("Odesláno 3 žákům ve třídě.", SchoolType.Secondary, "Odesláno 3 studentům ve třídě.")]
    [InlineData("Žádní studenti.", SchoolType.Elementary, "Žádní žáci.")]
    [InlineData("Nový student", SchoolType.Elementary, "Nový žák")]
    [InlineData("Žákovská knížka", SchoolType.Secondary, "Žákovská knížka")]
    [InlineData("Studentský průkaz", SchoolType.Elementary, "Studentský průkaz")]
    public void LocalizesEveryFormAndLeavesOtherWordsAlone(string text, SchoolType type, string expected) =>
        Assert.Equal(expected, StudentTerm.Localize(text, type));
}
