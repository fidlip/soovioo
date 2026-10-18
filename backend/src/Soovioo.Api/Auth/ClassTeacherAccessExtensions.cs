using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

/// <summary>"Is this Teacher the class teacher (třídní učitel) of this
/// class" — strictly <see cref="Evidence.SchoolClass.ClassTeacherId"/>,
/// deliberately narrower than <see cref="TeacherClassAccessExtensions"/>
/// (which also counts "has a timetable entry there"). Used to scope the
/// restricted Administrace access a class teacher gets: editing their own
/// class's students and parent↔student links, nothing else — see
/// ROADMAP "Administrace — redesign".</summary>
public static class ClassTeacherAccessExtensions
{
    public static async Task<List<int>> GetOwnClassIdsAsClassTeacherAsync(this AppDbContext db, int teacherId) =>
        await db.SchoolClasses.Where(c => c.ClassTeacherId == teacherId).Select(c => c.Id).ToListAsync();

    public static async Task<bool> IsClassTeacherOfAsync(this AppDbContext db, int teacherId, int schoolClassId) =>
        await db.SchoolClasses.AnyAsync(c => c.Id == schoolClassId && c.ClassTeacherId == teacherId);
}
