using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Grades;

[Authorize]
[ApiController]
[Route("api/students/{studentId}/report-card")]
public class ReportCardController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetReportCard(int studentId)
    {
        if (!await User.CanAccessStudentAsync(db, studentId))
        {
            return Forbid();
        }

        var student = await db.Students
            .Include(s => s.SchoolClass)
            .FirstOrDefaultAsync(s => s.Id == studentId);
        if (student is null)
        {
            return NotFound();
        }

        var gradesBySubject = await db.Grades
            .Where(g => g.StudentId == studentId)
            .GroupBy(g => g.Subject!.Name)
            .Select(g => new
            {
                Subject = g.Key,
                WeightedSum = g.Sum(x => x.Value * x.Weight),
                TotalWeight = g.Sum(x => x.Weight),
            })
            .OrderBy(x => x.Subject)
            .ToListAsync();

        var pdfBytes = ReportCardDocument.Generate(
            student,
            gradesBySubject
                .Select(g => (g.Subject, Average: g.WeightedSum / (double)g.TotalWeight))
                .ToList());

        return File(pdfBytes, "application/pdf", $"vysvedceni-{student.LastName}.pdf");
    }
}
