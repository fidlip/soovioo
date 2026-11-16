using Soovioo.Api.Common;

namespace Soovioo.Api.Tests.Common;

public class ContactFormatsTests
{
    [Theory]
    [InlineData("user@example.com", "user@example.com")]
    [InlineData("  USER@EXAMPLE.COM  ", "USER@example.com")]
    [InlineData("test@sub.example.com", "test@sub.example.com")]
    [InlineData("user+tag@example.com", "user+tag@example.com")]
    [InlineData("user.name@example.com", "user.name@example.com")]
    [InlineData("user_name@example.com", "user_name@example.com")]
    [InlineData("u@e.cz", "u@e.cz")]
    [InlineData("jan@škola-žižkov.cz", "jan@škola-žižkov.cz")]
    public void ValidEmails(string input, string expected)
    {
        Assert.True(ContactFormats.TryEmail(input, out var email));
        Assert.Equal(expected, email);
    }

    [Theory]
    [InlineData("userexample.com")]
    [InlineData("user@@example.com")]
    [InlineData("a@b@example.com")]
    [InlineData("user @example.com")]
    [InlineData("user@example")]
    [InlineData("user@example.c")]
    [InlineData("user@example.123")]
    [InlineData(".user@example.com")]
    [InlineData("user.@example.com")]
    [InlineData("user..name@example.com")]
    [InlineData("user@.example.com")]
    [InlineData("user@example..com")]
    [InlineData("user@example.com.")]
    [InlineData("user@-example.com")]
    [InlineData("user,x@example.com")]
    [InlineData("@example.com")]
    public void InvalidEmails(string input)
    {
        Assert.False(ContactFormats.TryEmail(input, out var email));
        Assert.Null(email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyEmailIsValidNull(string? input)
    {
        Assert.True(ContactFormats.TryEmail(input, out var email));
        Assert.Null(email);
    }

    [Theory]
    [InlineData("777123456", "+420 777 123 456")]
    [InlineData("777 123 456", "+420 777 123 456")]
    [InlineData("+420777123456", "+420 777 123 456")]
    [InlineData("+420 777 123 456", "+420 777 123 456")]
    [InlineData("00420 777 123 456", "+420 777 123 456")]
    [InlineData("420777123456", "+420 777 123 456")]
    [InlineData("777-123-456", "+420 777 123 456")]
    [InlineData("  234 567 890 ", "+420 234 567 890")]
    [InlineData("+421 905 123 456", "+421 905 123 456")]
    [InlineData("+421905-123/456", "+421905 123 456")]
    [InlineData("0049 30 1234567", "+49 30 1234567")]
    [InlineData("+44(20)7946 0958", "+44 20 7946 0958")]
    [InlineData("+ 421 905 123 456", "+421 905 123 456")]
    public void ValidPhones(string input, string expected)
    {
        Assert.True(ContactFormats.TryPhone(input, out var phone));
        Assert.Equal(expected, phone);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("777 123 45a")]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("077712345")]
    [InlineData("177123456")]
    [InlineData("+42077712345")]
    [InlineData("+4207771234567")]
    [InlineData("+420177123456")]
    [InlineData("+0123456789")]
    [InlineData("+1234567")]
    [InlineData("+1234567890123456")]
    [InlineData("777*123*456")]
    [InlineData("777+123456")]
    [InlineData("42017712345 6")]
    [InlineData("0042")]
    public void InvalidPhones(string input)
    {
        Assert.False(ContactFormats.TryPhone(input, out var phone));
        Assert.Null(phone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPhoneIsValidNull(string? input)
    {
        Assert.True(ContactFormats.TryPhone(input, out var phone));
        Assert.Null(phone);
    }
}
