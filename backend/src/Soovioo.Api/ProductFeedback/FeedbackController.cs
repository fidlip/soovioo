using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Support;

namespace Soovioo.Api.ProductFeedback;

/// <summary>"Zpětná vazba" in Nastavení — any user of a school.</summary>
[Authorize]
[ApiController]
[Route("api/feedback")]
[AuditArea("Zpětná vazba")]
public class FeedbackController(AppDbContext db, ICurrentSchoolContext currentSchool, FeedbackService feedback)
    : ControllerBase
{
    [HttpPost]
    [AuditAction("Odeslána zpětná vazba")]
    public async Task<IActionResult> Send(FeedbackRequest request)
    {
        // A SuperAdmin in support mode isn't the school's user.
        if (User.HasClaim(c => c.Type == SupportClaims.SessionId))
        {
            return Forbid();
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId);
        if (await feedback.SaveAsync(school, User.UserId(), request) is { } error)
        {
            return this.FieldError(error.Field, error.Message);
        }
        return NoContent();
    }
}
