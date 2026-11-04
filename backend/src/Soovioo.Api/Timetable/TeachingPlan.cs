using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>A version's učební plán and přidělení as <see cref="TeachingPlanner"/>
/// sees them — for Přidělení výuky and the generator.</summary>
public record TeachingPlan(
    List<TeachingClassDto> Classes, List<TeachingTeacherDto> Teachers, List<int> Grades, List<CurriculumItemDto> Curriculum)
{
    public static async Task<TeachingPlan> LoadAsync(AppDbContext db, TimetableVersion version)
    {
        var classes = await db.SchoolClasses.OrderBy(c => c.Name)
            .Select(c => new TeachingPlanner.ClassInput(c.Id, c.Name, c.Rocnik, c.ClassTeacherId))
            .ToListAsync();
        var subjects = await db.Subjects.OrderBy(s => s.Name)
            .Select(s => new TeachingPlanner.SubjectInput(s.Id, s.Name, s.Abbreviation, s.DoubleLessons))
            .ToListAsync();
        var curriculum = await db.CurriculumItems.Where(c => c.TimetableVersionId == version.Id)
            .OrderBy(c => c.Grade).ThenBy(c => c.SubjectId)
            .Select(c => new CurriculumItemDto(c.Grade, c.SubjectId, c.HoursPerWeek, c.DoubleLessons))
            .ToListAsync();
        var classCurriculum = await db.ClassCurriculumItems.Where(c => c.TimetableVersionId == version.Id)
            .Select(c => new ClassCurriculumItemDto(c.SchoolClassId, c.SubjectId, c.HoursPerWeek, c.DoubleLessons))
            .ToListAsync();
        var assignments = (await db.TeachingAssignments.Where(a => a.TimetableVersionId == version.Id)
                .Select(a => new { a.SchoolClassId, a.SubjectId, a.TeacherId })
                .ToListAsync())
            .Select(a => (a.SchoolClassId, a.SubjectId, a.TeacherId))
            .ToList();
        var lessons = await db.TimetableEntries.Where(e => e.TimetableVersionId == version.Id)
            .Select(e => new TeachingPlanner.LessonInput(e.SchoolClassId, e.SubjectId, e.TeacherId, e.DayOfWeek, e.WeekParity, e.PeriodNumber))
            .ToListAsync();

        var involved = assignments.Select(a => a.TeacherId)
            .Concat(lessons.Where(l => l.TeacherId is not null).Select(l => l.TeacherId!.Value))
            .Distinct().ToList();
        var users = await db.Users
            .Include(u => u.Person).ThenInclude(p => p!.TeacherProfile).ThenInclude(t => t!.Qualifications).ThenInclude(q => q.Subject)
            .Where(u => involved.Contains(u.Id)
                || (u.DeactivatedAt == null && u.Roles.Any(r => r.BuiltIn == UserRole.Teacher)))
            .AsSplitQuery()
            .ToListAsync();
        var teachers = users.Select(u =>
        {
            var profile = u.Person?.TeacherProfile;
            return new TeachingPlanner.TeacherInput(
                u.Id, PersonNames.Display(u),
                u.DeactivatedAt == null && u.Roles.Any(r => r.BuiltIn == UserRole.Teacher),
                profile?.Qualifications ?? [], profile?.Describe(), profile?.ContractedHours,
                profile?.Availability ?? [], profile is not null);
        }).ToList();

        var (classDtos, teacherDtos) = TeachingPlanner.Plan(new TeachingPlanner.Input(
            classes, subjects, teachers, curriculum, classCurriculum, assignments, lessons));

        var grades = classes.Where(c => c.Grade is not null).Select(c => c.Grade!.Value)
            .Concat(curriculum.Select(c => c.Grade)).Distinct().Order().ToList();
        return new TeachingPlan(classDtos, teacherDtos, grades, curriculum);
    }
}
