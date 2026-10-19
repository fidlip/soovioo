using System.Numerics;
using System.Text.RegularExpressions;

namespace Soovioo.Api.Evidence;

/// <summary>School bank account → IBAN (what the QR payment needs). Accepts
/// an IBAN (spaces allowed) or the Czech domestic format
/// <c>[předčíslí-]číslo/kód banky</c>, which converts deterministically:
/// <c>CZ kk BBBB PPPPPP CCCCCCCCCC</c>.</summary>
public static partial class BankAccounts
{
    [GeneratedRegex(@"^(?:(\d{1,6})-)?(\d{2,10})/(\d{4})$")]
    private static partial Regex CzechFormat();

    [GeneratedRegex(@"^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$")]
    private static partial Regex IbanFormat();

    private static readonly int[] CzechWeights = [6, 3, 7, 9, 10, 5, 8, 4, 2, 1];

    /// <summary>Compact IBAN (no spaces), or an error message for the user.</summary>
    public static (string? Iban, string? Error) ToIban(string input)
    {
        var compact = Regex.Replace(input, @"\s", "").ToUpperInvariant();

        var czech = CzechFormat().Match(compact);
        if (czech.Success)
        {
            var prefix = czech.Groups[1].Value.PadLeft(6, '0');
            var number = czech.Groups[2].Value.PadLeft(10, '0');
            var bank = czech.Groups[3].Value;
            if (!PassesMod11(prefix) || !PassesMod11(number))
            {
                return (null, "Číslo účtu není platné (nesedí kontrolní součet).");
            }
            var bban = bank + prefix + number;
            var check = 98 - Mod97(bban + "CZ00");
            return ($"CZ{check:00}{bban}", null);
        }

        if (IbanFormat().IsMatch(compact))
        {
            return Mod97(compact[4..] + compact[..4]) == 1
                ? (compact, null)
                : (null, "IBAN není platný (nesedí kontrolní číslice).");
        }

        return (null, "Zadejte IBAN (CZ65 0800 …) nebo číslo účtu ve tvaru 123456789/0800.");
    }

    /// <summary>"CZ6508000000192000145399" → "CZ65 0800 0000 1920 0014 5399".</summary>
    public static string Format(string iban) =>
        string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4)
            .Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4))));

    /// <summary>Czech account number check: weighted digit sum divisible by 11
    /// (weights for a zero-padded 10-digit number; a 6-digit prefix uses the
    /// last six).</summary>
    private static bool PassesMod11(string digits)
    {
        var weights = CzechWeights[^digits.Length..];
        var sum = digits.Select((d, i) => (d - '0') * weights[i]).Sum();
        return sum % 11 == 0;
    }

    /// <summary>ISO 7064 mod 97 with letters as 10..35.</summary>
    private static int Mod97(string value)
    {
        var numeric = string.Concat(value.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));
        return (int)(BigInteger.Parse(numeric) % 97);
    }
}
