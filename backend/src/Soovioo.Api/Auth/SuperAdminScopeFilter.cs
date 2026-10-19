using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Soovioo.Api.Auth;

/// <summary>Marks an endpoint a SuperAdmin may use even though it isn't
/// restricted to <c>Roles = "SuperAdmin"</c> (e.g. their own profile).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AllowSuperAdminAttribute : Attribute;

/// <summary>A SuperAdmin token bypasses the per-school query filters (it's
/// meant for cross-school management) — so on an ordinary school endpoint
/// it would see every school's data at once. Global filter: a SuperAdmin
/// only gets through to endpoints that name the SuperAdmin role, carry
/// <see cref="AllowSuperAdminAttribute"/>, or need no authentication at all.
/// Working inside a school goes through support mode (an Admin token).</summary>
public class SuperAdminScopeFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.User.IsInRole(nameof(UserRole.SuperAdmin)))
        {
            return;
        }

        var metadata = context.ActionDescriptor.EndpointMetadata;
        var authorizeData = metadata.OfType<IAuthorizeData>().ToList();
        var allowed =
            authorizeData.Count == 0
            || metadata.OfType<IAllowAnonymous>().Any()
            || metadata.OfType<AllowSuperAdminAttribute>().Any()
            || authorizeData.Any(a => a.Roles?.Split(',').Any(r => r.Trim() == nameof(UserRole.SuperAdmin)) == true);
        if (!allowed)
        {
            context.Result = new ForbidResult();
        }
    }
}
