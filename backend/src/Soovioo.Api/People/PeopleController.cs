using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.People;

/// <summary>Administrace → Osoby: the people of the school — staff, parents
/// and pupils — with their profiles, whether or not they have an account
/// (accounts are Uživatelé, each one is a Person's). Former pupils are in
/// Archiv a výmaz údajů, erased accounts nowhere.</summary>
[Authorize]
[RequirePermission(Permission.Administration)]
[ApiController]
[Route("api/people")]
public class PeopleController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PersonDto>>> GetPeople([FromServices] PhotoAccess? photoAccess = null)
    {
        var people = await (await ListedAsync())
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .ToListAsync();
        return Ok(await ToDtosAsync(people, photoAccess));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PersonDto>> GetPerson(int id, [FromServices] PhotoAccess? photoAccess = null)
    {
        var person = await (await ListedAsync()).FirstOrDefaultAsync(p => p.Id == id);
        return person is null ? NotFound() : Ok((await ToDtosAsync([person], photoAccess))[0]);
    }

    [HttpPost]
    public async Task<ActionResult<PersonDto>> AddPerson(
        SavePersonRequest request, [FromServices] PhotoAccess? photoAccess = null)
    {
        var person = new Person { SchoolId = currentSchool.SchoolId!.Value };
        db.Persons.Add(person);
        if (await ApplyAsync(person, request) is { } error)
        {
            return error;
        }
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetPerson), new { id = person.Id }, (await ToDtosAsync([person], photoAccess))[0]);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PersonDto>> UpdatePerson(
        int id, SavePersonRequest request,
        [FromServices] PhotoStorage? photos = null, [FromServices] PhotoAccess? photoAccess = null)
    {
        var person = await (await ListedAsync()).FirstOrDefaultAsync(p => p.Id == id);
        if (person is null)
        {
            return NotFound();
        }
        if (await ApplyAsync(person, request) is { } error)
        {
            return error;
        }
        await db.SaveChangesAsync();
        photos?.Delete(removedPhotoKey);
        return Ok((await ToDtosAsync([person], photoAccess))[0]);
    }

    /// <summary>Only someone without an account (delete that first); a pupil
    /// only without history, like in Žáci.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePerson(int id, [FromServices] PhotoStorage? photos = null)
    {
        var person = await (await ListedAsync()).FirstOrDefaultAsync(p => p.Id == id);
        if (person is null)
        {
            return NotFound();
        }
        if (person.UserId is not null)
        {
            return Conflict("Osoba má uživatelský účet — nejdřív ho smažte v záložce Uživatelé.");
        }
        if (person.Student is { } student)
        {
            var history = await StudentRecords.HistoryOfAsync(db, student);
            if (history.Count > 0)
            {
                return Conflict(await StudentRecords.RefusalAsync(db, student, history));
            }
            await StudentRecords.DeleteAsync(db, student, photos);
            return NoContent();
        }

        // Loaded with the person, so removed with it rather than in bulk —
        // a bulk delete would leave them tracked as still there.
        if (person.ParentProfile is { } parent)
        {
            db.ParentStudents.RemoveRange(parent.Children);
        }
        await db.DeleteAsync(db.VisibilityChoices.Where(c => c.PersonId == person.Id));
        db.Persons.Remove(person);
        await db.SaveChangesAsync();
        photos?.Delete(person.PhotoKey);
        return NoContent();
    }

    private async Task<IQueryable<Person>> ListedAsync()
    {
        // A former pupil is hidden with their record (query filter on
        // Students) — their Person goes with it. Asked separately: an
        // IgnoreQueryFilters() inside the query would switch the filters off
        // for all of it.
        var formerPupils = await db.Students.IgnoreQueryFilters()
            .Where(s => s.SchoolId == currentSchool.SchoolId && s.LeftAt != null && s.LeftAt < db.Today)
            .Select(s => s.PersonId)
            .ToListAsync();
        return db.Persons
            .Include(p => p.TeacherProfile).ThenInclude(t => t!.Qualifications).ThenInclude(q => q.Subject)
            .Include(p => p.ParentProfile).ThenInclude(pp => pp!.Children)
            .Include(p => p.Student).ThenInclude(st => st!.SchoolClass)
            .Include(p => p.User)
            .Where(p => p.User == null || p.User.ErasedAt == null)
            .Where(p => !formerPupils.Contains(p.Id));
    }

    /// <summary>The photo of a pupil's profile <see cref="ApplyAsync"/>
    /// removed — deleted once that's saved.</summary>
    private string? removedPhotoKey;

    /// <summary>Name, contacts and the profiles the request has; a profile
    /// left out is removed — except one the account's role needs, and a
    /// pupil's with history (that pupil's education is ended instead).</summary>
    private async Task<ActionResult?> ApplyAsync(Person person, SavePersonRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            return this.FieldError("lastName", "Příjmení je povinné.");
        }
        var roles = person.User?.Roles ?? [];
        if (request.Teacher is null && person.TeacherProfile is not null && roles.Any(r => r.BuiltIn == UserRole.Teacher))
        {
            return BadRequest("Účet má roli Učitel — profil učitele jde odebrat až s ní (Uživatelé).");
        }
        if (request.Parent is null && person.ParentProfile is not null && roles.Any(r => r.Kind == RoleKind.Parent))
        {
            return BadRequest("Účet má roli Rodič — profil rodiče jde odebrat až s ní (Uživatelé).");
        }
        if (request.Student is null && person.Student is { } removed)
        {
            if (roles.Any(r => r.Kind == RoleKind.Student))
            {
                return BadRequest(StudentTerm.Localize(
                    "Účet má roli Žák — profil žáka jde odebrat až s ní (Uživatelé).", await SchoolTypeAsync()));
            }
            var history = await StudentRecords.HistoryOfAsync(db, removed);
            if (history.Count > 0)
            {
                return Conflict(await StudentRecords.RefusalAsync(db, removed, history, profileOnly: true));
            }
        }
        if (request.Student is { } matrika && !await db.SchoolClasses.AnyAsync(c => c.Id == matrika.SchoolClassId))
        {
            return this.FieldError("schoolClassId", "Neznámá třída.");
        }
        if (request.Teacher is { } requested && await TeacherErrorAsync(requested) is { } teacherError)
        {
            return teacherError;
        }
        var childIds = request.Parent?.ChildIds.Distinct().ToList() ?? [];
        if (childIds.Count > 0 && await db.Students.CountAsync(s => childIds.Contains(s.Id)) != childIds.Count)
        {
            return this.FieldError("childIds", StudentTerm.Localize("Neznámý žák.", await SchoolTypeAsync()));
        }
        if (person.Student is { } own && childIds.Contains(own.Id))
        {
            return this.FieldError("childIds", StudentTerm.Localize("Žák nemůže být sám sobě rodičem.", await SchoolTypeAsync()));
        }

        person.FirstName = request.FirstName?.Trim() ?? "";
        person.LastName = request.LastName.Trim();
        person.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        person.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var schoolId = currentSchool.SchoolId!.Value;

        if (request.Teacher is { } teacher)
        {
            person.TeacherProfile ??= new TeacherProfile { Person = person, SchoolId = schoolId };
            var profile = person.TeacherProfile;
            profile.QualificationNote = string.IsNullOrWhiteSpace(teacher.QualificationNote) ? null : teacher.QualificationNote.Trim();
            profile.ContractedHours = teacher.ContractedHours;
            profile.TeachingDays = teacher.TeachingDays is { } days
                ? [.. TeacherProfile.Weekdays.Where(days.Contains)]
                : [.. TeacherProfile.Weekdays];
            var wanted = teacher.Qualifications ?? [];
            profile.Qualifications.RemoveAll(q => !wanted.Any(w => Same(w, q)));
            foreach (var w in wanted.Where(w => !profile.Qualifications.Any(q => Same(w, q))))
            {
                profile.Qualifications.Add(new TeacherQualification
                {
                    SchoolId = schoolId, SubjectId = w.SubjectId, GradeFrom = w.GradeFrom, GradeTo = w.GradeTo,
                    Subject = await db.Subjects.FindAsync(w.SubjectId),
                });
            }
        }
        else if (person.TeacherProfile is not null)
        {
            db.TeacherProfiles.Remove(person.TeacherProfile);
            person.TeacherProfile = null;
        }

        if (request.Parent is not null)
        {
            person.ParentProfile ??= new ParentProfile { Person = person, SchoolId = schoolId };
            person.ParentProfile.Children.RemoveAll(c => !childIds.Contains(c.StudentId));
            foreach (var childId in childIds.Where(id => person.ParentProfile.Children.All(c => c.StudentId != id)))
            {
                person.ParentProfile.Children.Add(new ParentStudent { StudentId = childId, SchoolId = schoolId });
            }
        }
        else if (person.ParentProfile is not null)
        {
            db.ParentStudents.RemoveRange(person.ParentProfile.Children);
            db.ParentProfiles.Remove(person.ParentProfile);
            person.ParentProfile = null;
        }

        if (request.Student is null && person.Student is { } dropped)
        {
            removedPhotoKey = await StudentRecords.RemoveProfileAsync(db, dropped);
        }
        if (request.Student is { } studentMatrika)
        {
            var student = person.Student;
            if (student is null)
            {
                student = new StudentProfile { Person = person, SchoolId = schoolId };
                db.Students.Add(student);
            }
            if (await StudentRecords.ApplyAsync(db, student, studentMatrika) is { } error)
            {
                return this.FieldError("defaultVariableSymbol", error);
            }
        }
        return null;
    }

    private static bool Same(QualificationDto w, TeacherQualification q) =>
        w.SubjectId == q.SubjectId && w.GradeFrom == q.GradeFrom && w.GradeTo == q.GradeTo;

    private async Task<ActionResult?> TeacherErrorAsync(TeacherProfileRequest teacher)
    {
        if (teacher.ContractedHours is < 0 or > 60)
        {
            return this.FieldError("contractedHours", "Nasmlouvané hodiny musí být mezi 0 a 60.");
        }
        var qualifications = teacher.Qualifications ?? [];
        var subjectIds = qualifications.Select(q => q.SubjectId).Distinct().ToList();
        if (await db.Subjects.CountAsync(s => subjectIds.Contains(s.Id)) != subjectIds.Count)
        {
            return this.FieldError("qualifications", "Neznámý předmět.");
        }
        if (qualifications.Any(q => q.GradeFrom is < 1 or > 13 || q.GradeTo is < 1 or > 13 || q.GradeFrom > q.GradeTo))
        {
            return this.FieldError("qualifications", "Ročníky aprobace musí být od 1 do 13 a „od“ nejvýš „do“.");
        }
        return null;
    }

    private async Task<SchoolType> SchoolTypeAsync() =>
        await db.Schools.Where(s => s.Id == currentSchool.SchoolId).Select(s => (SchoolType?)s.Type).FirstOrDefaultAsync()
            ?? SchoolType.Elementary;

    private async Task<List<PersonDto>> ToDtosAsync(List<Person> people, PhotoAccess? photoAccess)
    {
        var userIds = people.Where(p => p.UserId != null).Select(p => p.UserId!.Value).ToList();
        var classTeacherOf = (await db.SchoolClasses
                .Where(c => c.ClassTeacherId != null && userIds.Contains(c.ClassTeacherId.Value))
                .Select(c => new { TeacherId = c.ClassTeacherId!.Value, c.Name })
                .ToListAsync())
            .ToLookup(c => c.TeacherId, c => c.Name);
        var childIds = people.SelectMany(p => p.ParentProfile?.Children.Select(c => c.StudentId) ?? []).Distinct().ToList();
        var childNames = await db.Students
            .Where(s => childIds.Contains(s.Id))
            .Select(s => new { s.Id, Name = s.Person!.FirstName + " " + s.Person.LastName })
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        Dictionary<int, string> userPhotos = [];
        Dictionary<int, string> studentPhotos = [];
        if (photoAccess is not null)
        {
            userPhotos = await photoAccess.VisibleUserPhotosAsync(
                User, people.Where(p => p.User is not null && p.Student is null).Select(p => p.User!));
            studentPhotos = await photoAccess.VisibleStudentPhotosAsync(
                User, people.Where(p => p.Student is not null).Select(p => p.Student!));
        }

        var result = new List<PersonDto>();
        foreach (var p in people)
        {
            result.Add(new PersonDto(
                p.Id, p.FirstName, p.LastName, p.FullName, p.Phone, p.Email,
                p.Student is { } st ? studentPhotos.GetValueOrDefault(st.Id) : p.UserId is { } uid ? userPhotos.GetValueOrDefault(uid) : null,
                p.User is { } u ? new PersonAccountDto(u.Id, u.Username, RoleRefDto.ListOf(u.Roles), u.DeactivatedAt) : null,
                p.TeacherProfile is { } t
                    ? new TeacherProfileDto(
                        t.Describe(),
                        t.Qualifications.OrderBy(q => q.Subject?.Name).Select(QualificationDto.Of).ToList(),
                        t.QualificationNote, t.ContractedHours, t.TeachingDays,
                        p.UserId is { } teacherId ? classTeacherOf[teacherId].ToList() : [])
                    : null,
                p.ParentProfile is { } pp
                    ? new ParentProfileDto(pp.Children
                        .Where(c => childNames.ContainsKey(c.StudentId))
                        .Select(c => new PersonChildDto(c.StudentId, childNames[c.StudentId]))
                        .OrderBy(c => c.Name)
                        .ToList())
                    : null,
                p.Student is { } s ? await StudentRecords.ToDtoAsync(db, s) : null));
        }
        return result;
    }
}

public record SavePersonRequest(
    string? FirstName,
    string LastName,
    string? Phone = null,
    string? Email = null,
    TeacherProfileRequest? Teacher = null,
    ParentProfileRequest? Parent = null,
    StudentMatrika? Student = null);

public record TeacherProfileRequest(
    IReadOnlyList<QualificationDto>? Qualifications = null,
    string? QualificationNote = null,
    decimal? ContractedHours = null,
    /// <summary>Null = every weekday.</summary>
    IReadOnlyList<DayOfWeek>? TeachingDays = null);

/// <param name="GradeFrom">Ročník od; null = from the first.</param>
/// <param name="GradeTo">Ročník do; null = to the last.</param>
public record QualificationDto(int SubjectId, int? GradeFrom = null, int? GradeTo = null)
{
    public static QualificationDto Of(TeacherQualification q) => new(q.SubjectId, q.GradeFrom, q.GradeTo);
}

public record ParentProfileRequest(IReadOnlyList<int> ChildIds);

/// <param name="Photo">Token of the pupil's photo for a pupil, of the
/// account's otherwise.</param>
public record PersonDto(
    int Id,
    string FirstName,
    string LastName,
    string Name,
    string? Phone,
    string? Email,
    string? Photo,
    PersonAccountDto? Account,
    TeacherProfileDto? Teacher,
    ParentProfileDto? Parent,
    StudentDto? Student);

public record PersonAccountDto(int UserId, string Username, IReadOnlyList<RoleRefDto> Roles, DateTimeOffset? DeactivatedAt);

/// <param name="Qualification">The aprobace as text, e.g. "D 6.–9., Čj".</param>
/// <param name="ClassTeacherOf">Classes whose class teacher the person's account is.</param>
public record TeacherProfileDto(
    string? Qualification,
    IReadOnlyList<QualificationDto> Qualifications,
    string? QualificationNote,
    decimal? ContractedHours,
    IReadOnlyList<DayOfWeek> TeachingDays,
    IReadOnlyList<string> ClassTeacherOf);

public record ParentProfileDto(IReadOnlyList<PersonChildDto> Children);

public record PersonChildDto(int StudentId, string Name);
