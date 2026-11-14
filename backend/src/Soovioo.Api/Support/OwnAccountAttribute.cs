using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Soovioo.Api.Support;

/// <summary>Marks an endpoint about the caller's own account (profile,
/// password, calendar feed, notification settings…). Support mode has no
/// such account — its token carries the SuperAdmin's id inside the visited
/// school — so <see cref="SupportModeScopeFilter"/> refuses it there.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class OwnAccountAttribute : Attribute;

/// <summary>Global filter: 403 for <see cref="OwnAccountAttribute"/>
/// endpoints called with a support-mode token.</summary>
public class SupportModeScopeFilter : IAuthorizationFilter
{
    public const string Refusal = "V režimu podpory není vlastní účet k dispozici.";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.FindFirst(SupportClaims.SessionId) is not null
            && context.ActionDescriptor.EndpointMetadata.OfType<OwnAccountAttribute>().Any())
        {
            context.Result = new ObjectResult(Refusal) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
