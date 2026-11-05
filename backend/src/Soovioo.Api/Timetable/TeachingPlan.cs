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
    public List<TeachingGroupDto> Groups { get; init; } = [];

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
        var lessons = (await db.TimetableEntries.Where(e => e.TimetableVersionId == version.Id)
                .OrderBy(e => e.SchoolClassId)
                .Select(e => new TeachingPlanner.LessonInput(
                    e.SchoolClassId, e.SubjectId, e.TeacherId, e.DayOfWeek, e.WeekParity, e.PeriodNumber, e.StudentGroupId))
                .ToListAsync())
            .ToList();
        // A group's lesson once, not once per class.
        lessons = [.. lessons.Where(l => l.GroupId is null)
            .Concat(lessons.Where(l => l.GroupId is not null).DistinctBy(l => (l.GroupId, l.DayOfWeek, l.Period, l.WeekParity)))];
        var groups = await GroupsAsync(db, version.Id);

        var involved = assignments.Select(a => a.TeacherId)
            .Concat(groups.Where(g => g.TeacherId is not null).Select(g => g.TeacherId!.Value))
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

        var (classDtos, teacherDtos, groupDtos) = TeachingPlanner.PlanWithGroups(new TeachingPlanner.Input(
            classes, subjects, teachers, curriculum, classCurriculum, assignments, lessons, groups));

        var grades = classes.Where(c => c.Grade is not null).Select(c => c.Grade!.Value)
            .Concat(curriculum.Select(c => c.Grade)).Distinct().Order().ToList();
        return new TeachingPlan(classDtos, teacherDtos, grades, curriculum) { Groups = groupDtos };
    }

    public static async Task<List<TeachingPlanner.GroupInput>> GroupsAsync(AppDbContext db, int versionId) =>
        (await db.StudentGroups.Where(g => g.TimetableVersionId == versionId)
            .Select(g => new
            {
                g.Id, g.Name, g.SubjectId, ClassIds = g.Classes.Select(c => c.SchoolClassId).ToList(), g.HoursPerWeek,
                DoubleLessons = g.DoubleLessons ?? g.Subject!.DoubleLessons, g.TeacherId, g.GroupDivisionId,
                DivisionName = g.GroupDivision == null ? null : g.GroupDivision.Name, Members = g.Members.Count,
                CoversSubjectId = g.GroupDivision == null ? null : g.GroupDivision.CoversSubjectId,
            })
            .ToListAsync())
        .Select(g => new TeachingPlanner.GroupInput(
            g.Id, g.Name, g.SubjectId, g.ClassIds, g.HoursPerWeek, g.DoubleLessons, g.TeacherId, g.GroupDivisionId,
            g.DivisionName, g.Members, g.CoversSubjectId))
        .ToList();
}
