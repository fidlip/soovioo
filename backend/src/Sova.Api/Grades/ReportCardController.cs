using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
            .Select(g => new { Subject = g.Key, Average = g.Average(x => x.Value) })
            .OrderBy(x => x.Subject)
            .ToListAsync();

        var pdfBytes = ReportCardDocument.Generate(
            student,
            gradesBySubject.Select(g => (g.Subject, g.Average)).ToList());

        return File(pdfBytes, "application/pdf", $"vysvedceni-{student.LastName}.pdf");
    }
}
