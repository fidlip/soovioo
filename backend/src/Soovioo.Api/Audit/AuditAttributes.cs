using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Soovioo.Api.Audit;

/// <summary>Agenda of a controller's audited actions in the log ("Známky",
/// "Uživatelé"…). Required on every controller with an
/// <see cref="AuditActionAttribute"/> — the build fails without it.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AuditAreaAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Human-readable description of a data-changing action, without
/// its subject ("Vytvořen uživatel"); <see cref="AuditMiddleware"/> appends
/// the name of what was touched (from the payload) or its id, and the
/// action may refine it at run time via <see cref="AuditContext"/>. Every
/// POST/PUT/PATCH/DELETE action carries this or <see cref="NoAuditAttribute"/>
/// — the build fails otherwise.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditActionAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

/// <summary>The action changes nothing worth logging (marking as read,
/// registering a device, a read sent as POST) — no log entry at all.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NoAuditAttribute : Attribute;

/// <summary>All agendas and action descriptions declared on the
/// controllers.</summary>
public static class AuditCatalog
{
    private static readonly Type[] Controllers = typeof(AuditCatalog).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
        .ToArray();

    /// <summary>Agenda of an entry whose request matched no audited controller.</summary>
    public const string OtherArea = "Ostatní";

    /// <summary>All agenda labels, for the filter.</summary>
    public static IReadOnlyList<string> AllAreas { get; } = Controllers
        .Select(t => t.GetCustomAttribute<AuditAreaAttribute>()?.Name)
        .OfType<string>()
        .Distinct()
        .Order()
        .ToList();

    /// <summary>All action descriptions (without subject).</summary>
    public static IReadOnlyList<string> AllActions { get; } = Controllers
        .SelectMany(t => t.GetMethods())
        .Select(m => m.GetCustomAttribute<AuditActionAttribute>()?.Description)
        .OfType<string>()
        .Distinct()
        .ToList();
}
