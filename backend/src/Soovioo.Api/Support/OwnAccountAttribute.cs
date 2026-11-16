using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Soovioo.Api.Auth;

namespace Soovioo.Api.Support;

/// <summary>Marks an endpoint about the caller's own account (profile,
/// password, calendar feed, notification settings, linked accounts…).
/// Support mode has no such account — its token carries the SuperAdmin's
/// id inside the visited school, or the user's they entered as, whose
/// account it isn't theirs to manage — so <see cref="SupportModeScopeFilter"/>
/// refuses it there.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class OwnAccountAttribute : Attribute;

/// <summary>Something done in the user's name that reaches outside or has
/// legal weight (sending a message, writing an omluvenka): refused in
/// support mode as one of the school's users (<see cref="SupportModeScopeFilter"/>).</summary>
/// <param name="what">"posílat zprávy" — what can't be done.</param>
[AttributeUsage(AttributeTargets.Method)]
public class NotAsUserInSupportAttribute(string what) : Attribute
{
    public string What { get; } = what;

    /// <summary>Only in a family role (parent, pupil) — the school's staff may.</summary>
    public bool FamilyOnly { get; init; }
}

/// <summary>Marks something read for the caller (a notification, a
/// conversation, a behaviour record). In support mode it changes nothing
/// — the user's unread stays unread — and answers 204
/// (<see cref="SupportModeScopeFilter"/>).</summary>
[AttributeUsage(AttributeTargets.Method)]
public class MarksReadAttribute : Attribute;

/// <summary>Global filter: 403 for <see cref="OwnAccountAttribute"/>
/// endpoints called with a support-mode token, and for
/// <see cref="NotAsUserInSupportAttribute"/> ones as a school's user;
/// <see cref="MarksReadAttribute"/> ones do nothing there.</summary>
public class SupportModeScopeFilter : IAuthorizationFilter
{
    public const string Refusal = "V režimu podpory není vlastní účet k dispozici.";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (!user.InSupportMode())
        {
            return;
        }
        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<OwnAccountAttribute>().Any())
        {
            context.Result = new ObjectResult(Refusal) { StatusCode = StatusCodes.Status403Forbidden };
        }
        else if (user.InSupportAsUser()
                 && metadata.OfType<NotAsUserInSupportAttribute>().FirstOrDefault() is { } notAsUser
                 && (!notAsUser.FamilyOnly || !user.IsStaff()))
        {
            context.Result = new ObjectResult($"V režimu podpory jménem uživatele nejde {notAsUser.What}.")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
        else if (metadata.OfType<MarksReadAttribute>().Any())
        {
            var audit = Audit.AuditContext.Of(context.HttpContext);
            audit.Description = "Přečteno v režimu podpory — u uživatele zůstává nepřečtené";
            context.Result = new NoContentResult();
        }
    }
}
