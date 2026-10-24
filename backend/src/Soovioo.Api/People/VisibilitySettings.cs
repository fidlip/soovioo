using Soovioo.Api.Data;

namespace Soovioo.Api.People;

/// <summary>Someone's visibility settings as offered next to their data
/// (Nastavení → Profil, Mé děti), and saving a choice.</summary>
public static class VisibilitySettings
{
    public static IReadOnlyList<VisibilitySettingDto> Of(
        SchoolVisibility visibility, IEnumerable<VisibilityOwner> owners, IReadOnlyList<VisibilityChoice> choices, bool editable) =>
        owners
            .SelectMany(owner => Visibility.Keys.Where(k => k.Owner == owner))
            .Select(key =>
            {
                var rule = visibility.Rule(key);
                return new VisibilitySettingDto(
                    key.Owner, key.Field, key.Audience, visibility.Effective(key, choices),
                    rule.Min, rule.Default, rule.Max, choices.Any(c => c.Key == key), editable);
            })
            .ToList();

    /// <summary>Stores <paramref name="request"/> among <paramref name="choices"/>
    /// (those of the person or pupil <paramref name="newChoice"/> creates a
    /// row for) — or drops the row when it's back at the school's default.
    /// Returns an error message, null on success; the caller saves.</summary>
    public static string? Choose(
        AppDbContext db, SchoolVisibility visibility, IReadOnlyCollection<VisibilityOwner> owners,
        List<VisibilityChoice> choices, SetVisibilityRequest request, Func<VisibilityChoice> newChoice)
    {
        var key = new VisibilityKey(request.Owner, request.Field, request.Audience);
        if (!owners.Contains(key.Owner) || !Visibility.Keys.Contains(key) || !Enum.IsDefined(request.Scope))
        {
            return "Tento údaj nastavit nemůžete.";
        }
        var rule = visibility.Rule(key);
        if (request.Scope < rule.Min || request.Scope > rule.Max)
        {
            return "Takovou viditelnost škola nepovoluje.";
        }

        var existing = choices.FirstOrDefault(c => c.Key == key);
        if (request.Scope == rule.Default)
        {
            if (existing is not null)
            {
                db.VisibilityChoices.Remove(existing);
                choices.Remove(existing);
            }
            return null;
        }
        if (existing is null)
        {
            existing = newChoice();
            existing.Owner = key.Owner;
            existing.Field = key.Field;
            existing.Audience = key.Audience;
            db.VisibilityChoices.Add(existing);
            choices.Add(existing);
        }
        existing.Scope = request.Scope;
        return null;
    }
}

/// <param name="Scope">What applies now — the choice or the default,
/// within the school's range and the name's scope.</param>
/// <param name="Custom">The person chose it themselves (vs. the default).</param>
public record VisibilitySettingDto(
    VisibilityOwner Owner, VisibilityField Field, VisibilityAudience Audience, VisibilityScope Scope,
    VisibilityScope Min, VisibilityScope Default, VisibilityScope Max, bool Custom, bool Editable);

public record SetVisibilityRequest(
    VisibilityOwner Owner, VisibilityField Field, VisibilityAudience Audience, VisibilityScope Scope);
