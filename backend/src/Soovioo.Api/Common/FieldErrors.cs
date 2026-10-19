using Microsoft.AspNetCore.Mvc;

namespace Soovioo.Api.Common;

public static class FieldErrors
{
    /// <summary>400 with a ValidationProblemDetails naming the offending
    /// field (camelCase, as in the request JSON) — the web client marks that
    /// field invalid and shows the message under it, same shape as ASP.NET's
    /// automatic model validation. Built by hand (not ControllerBase.
    /// ValidationProblem) so it works without an HttpContext in unit tests.</summary>
    public static BadRequestObjectResult FieldError(this ControllerBase _, string field, string message) =>
        new(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = message,
        });
}
