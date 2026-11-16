using System.Text;

namespace Soovioo.Api.Common;

/// <summary>Checks and normalizes e-mails and phone numbers people type in
/// forms. An empty value is valid and stored as null.</summary>
public static class ContactFormats
{
    public const string InvalidEmail = "Neplatný e-mail.";
    public const string InvalidPhone = "Neplatný telefon — české číslo (777 123 456) nebo zahraniční s předvolbou (+421 …).";

    private const string CzechPrefix = "420";

    /// <summary>Trimmed, with the domain in lower case.</summary>
    public static bool TryEmail(string? input, out string? email)
    {
        email = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }
        input = input.Trim();
        if (input.Length > 254 || input.Any(c => char.IsWhiteSpace(c) || "<>,;\"()".Contains(c)))
        {
            return false;
        }

        var at = input.IndexOf('@');
        if (at < 0 || at != input.LastIndexOf('@'))
        {
            return false;
        }
        var local = input[..at];
        var domain = input[(at + 1)..];
        if (local.Length is 0 or > 64 || local.StartsWith('.') || local.EndsWith('.') || local.Contains(".."))
        {
            return false;
        }

        var labels = domain.Split('.');
        if (labels.Length < 2 || labels.Any(l =>
                l.Length is 0 or > 63 || l.StartsWith('-') || l.EndsWith('-') ||
                l.Any(c => !char.IsLetterOrDigit(c) && c != '-')))
        {
            return false;
        }
        if (labels[^1].Length < 2 || !labels[^1].All(char.IsLetter))
        {
            return false;
        }

        email = $"{local}@{domain.ToLowerInvariant()}";
        return true;
    }

    /// <summary>Czech numbers as "+420 777 123 456" (with or without the
    /// prefix, also "00420…"); foreign ones need "+" or "00" and keep their
    /// grouping, separators turned into single spaces.</summary>
    public static bool TryPhone(string? input, out string? phone)
    {
        phone = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }
        input = input.Trim();

        var plus = input.StartsWith('+');
        var grouped = new StringBuilder();
        foreach (var c in plus ? input[1..] : input)
        {
            if (char.IsAsciiDigit(c))
            {
                grouped.Append(c);
            }
            else if (" -./()".Contains(c))
            {
                if (grouped.Length > 0 && grouped[^1] != ' ')
                {
                    grouped.Append(' ');
                }
            }
            else
            {
                return false;
            }
        }

        var groups = grouped.ToString().TrimEnd();
        if (!plus && groups.StartsWith("00"))
        {
            plus = true;
            groups = groups[2..].TrimStart();
        }
        var digits = groups.Replace(" ", "");

        var national = (plus, digits.Length) switch
        {
            (false, 9) => digits,
            (false, 12) when digits.StartsWith(CzechPrefix) => digits[3..],
            (true, _) when digits.StartsWith(CzechPrefix) => digits.Length == 12 ? digits[3..] : "",
            _ => null,
        };
        if (national is not null)
        {
            if (national.Length != 9 || national[0] is < '2' or > '9')
            {
                return false;
            }
            phone = $"+{CzechPrefix} {national[..3]} {national[3..6]} {national[6..]}";
            return true;
        }

        if (!plus || digits.Length is < 8 or > 15 || digits[0] == '0')
        {
            return false;
        }
        phone = "+" + groups;
        return true;
    }
}
