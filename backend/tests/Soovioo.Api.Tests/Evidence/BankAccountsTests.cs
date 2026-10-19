using Soovioo.Api.Evidence;

namespace Soovioo.Api.Tests.Evidence;

public class BankAccountsTests
{
    [Theory]
    [InlineData("19-2000145399/0800", "CZ6508000000192000145399")]
    [InlineData("CZ65 0800 0000 1920 0014 5399", "CZ6508000000192000145399")]
    [InlineData("cz6508000000192000145399", "CZ6508000000192000145399")]
    public void ConvertsCzechFormatAndAcceptsIban(string input, string expected)
    {
        var (iban, error) = BankAccounts.ToIban(input);
        Assert.Null(error);
        Assert.Equal(expected, iban);
    }

    [Theory]
    [InlineData("19-2000145398/0800")] // bad mod 11
    [InlineData("CZ6608000000192000145399")] // bad IBAN check digits
    [InlineData("hello")]
    public void RejectsInvalidAccounts(string input)
    {
        var (iban, error) = BankAccounts.ToIban(input);
        Assert.Null(iban);
        Assert.NotNull(error);
    }

    [Fact]
    public void FormatsInGroupsOfFour() =>
        Assert.Equal("CZ65 0800 0000 1920 0014 5399", BankAccounts.Format("CZ6508000000192000145399"));
}
