using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable.Rvp;

/// <summary>Fills a version's učební plán with what its classes' programs
/// propose (<see cref="RvpTemplates"/>): for the grade where its classes all
/// have one program, for each class where they differ.</summary>
public static class RvpFill
{
    public static async Task<RvpFillSummaryDto> RunAsync(AppDbContext db, TimetableVersion version, RvpFillOptions options, bool apply)
    {
        var classes = await db.SchoolClasses.OrderBy(c => c.Name).ToListAsync();
        var subjects = await db.Subjects.ToListAsync();
        var gradeItems = await db.CurriculumItems.Where(c => c.TimetableVersionId == version.Id).ToListAsync();
        var classItems = await db.ClassCurriculumItems.Where(c => c.TimetableVersionId == version.Id).ToListAsync();

        int added = 0, overwritten = 0, kept = 0, removed = 0;
        var created = new Dictionary<string, Subject>();
        var missing = new SortedSet<string>();
        var notes = new SortedSet<string>();
        var proposals = new Dictionary<string, RvpTemplates.Proposal>();

        RvpProgram? ProgramOf(SchoolClass c) => c.Rocnik is { } grade && RvpCatalog.Find(c.RvpProgramCode) is { } program
            && program.StageOf(grade) is not null ? program : null;
        var skipped = classes.Where(c => ProgramOf(c) is null).Select(c => c.Name).ToList();

        IReadOnlyDictionary<string, decimal> Proposed(RvpProgram program, int grade)
        {
            if (!proposals.TryGetValue(program.Code, out var proposal))
            {
                proposal = proposals[program.Code] = RvpTemplates.Propose(program, options.SpreadDisponible);
                notes.UnionWith(proposal.Notes);
            }
            return proposal.ByGrade[grade];
        }

        // The school's subject for a proposed one, a new one if it may be made.
        Subject? SubjectFor(string name)
        {
            var known = RvpSubjects.ByName(name);
            var existing = subjects.FirstOrDefault(s => RvpSubjects.Normalize(s.Name) == RvpSubjects.Normalize(name))
                ?? (known is null ? null : subjects.FirstOrDefault(s => RvpSubjects.Recognize(s.Name, s.Abbreviation) == known));
            if (existing is not null)
            {
                return existing;
            }
            if (!options.CreateSubjects)
            {
                missing.Add(name);
                return null;
            }
            if (!created.TryGetValue(name, out var subject))
            {
                subject = created[name] = new Subject { Name = name, Abbreviation = known?.Abbreviation, SchoolId = version.SchoolId };
                if (apply)
                {
                    db.Subjects.Add(subject);
                }
            }
            return subject;
        }

        void Merge<TItem>(
            IReadOnlyDictionary<string, decimal> proposed, List<TItem> current, Func<TItem, Subject?, bool> isOf, Func<TItem, decimal?> hours,
            Action<TItem, decimal> setHours, Func<Subject, decimal, TItem> create, Func<TItem, int> subjectIdOf, Action<TItem> remove)
        {
            var touched = new HashSet<int>();
            foreach (var (name, value) in proposed)
            {
                var subject = SubjectFor(name);
                if (subject is null)
                {
                    continue;
                }
                touched.Add(subject.Id);
                var item = current.FirstOrDefault(i => isOf(i, subject));
                if (item is null || hours(item) is null or 0)
                {
                    added++;
                    if (apply)
                    {
                        if (item is null)
                        {
                            create(subject, value);
                        }
                        else
                        {
                            setHours(item, value);
                        }
                    }
                }
                else if (hours(item) == value)
                {
                    kept++;
                }
                else if (options.Overwrite)
                {
                    overwritten++;
                    if (apply)
                    {
                        setHours(item, value);
                    }
                }
                else
                {
                    kept++;
                }
            }
            if (options.RemoveOthers)
            {
                foreach (var item in current.Where(i => !touched.Contains(subjectIdOf(i))))
                {
                    removed++;
                    if (apply)
                    {
                        remove(item);
                    }
                }
            }
        }

        foreach (var grade in classes.Where(c => ProgramOf(c) is not null).Select(c => c.Rocnik!.Value).Distinct().Order())
        {
            var inGrade = classes.Where(c => c.Rocnik == grade).ToList();
            var programsThere = inGrade.Select(c => ProgramOf(c)?.Code).Distinct().ToList();
            if (programsThere.Count == 1)
            {
                var items = gradeItems.Where(i => i.Grade == grade).ToList();
                Merge(
                    Proposed(ProgramOf(inGrade[0])!, grade), items,
                    (i, s) => i.Subject == s || (s is { Id: > 0 } && i.SubjectId == s.Id), i => i.HoursPerWeek, (i, h) => i.HoursPerWeek = h,
                    (s, h) =>
                    {
                        var item = new CurriculumItem
                        {
                            SchoolId = version.SchoolId, TimetableVersionId = version.Id, Grade = grade, Subject = s, HoursPerWeek = h,
                        };
                        db.CurriculumItems.Add(item);
                        return item;
                    },
                    i => i.SubjectId, i => db.CurriculumItems.Remove(i));
            }
            else
            {
                foreach (var schoolClass in inGrade.Where(c => ProgramOf(c) is not null))
                {
                    var items = classItems.Where(i => i.SchoolClassId == schoolClass.Id).ToList();
                    Merge(
                        Proposed(ProgramOf(schoolClass)!, grade), items,
                        (i, s) => i.Subject == s || (s is { Id: > 0 } && i.SubjectId == s.Id), i => i.HoursPerWeek, (i, h) => i.HoursPerWeek = h,
                        (s, h) =>
                        {
                            var item = new ClassCurriculumItem
                            {
                                SchoolId = version.SchoolId, TimetableVersionId = version.Id, SchoolClassId = schoolClass.Id, Subject = s, HoursPerWeek = h,
                            };
                            db.ClassCurriculumItems.Add(item);
                            return item;
                        },
                        i => i.SubjectId, i => db.ClassCurriculumItems.Remove(i));
                }
            }
        }

        if (apply)
        {
            await db.SaveChangesAsync();
        }
        return new RvpFillSummaryDto(added, overwritten, kept, removed, [.. created.Keys.Order()], [.. missing], skipped, [.. notes]);
    }
}

/// <param name="Overwrite">Hours already in the plan change to the proposed ones.</param>
/// <param name="CreateSubjects">Subjects the school doesn't have are made.</param>
/// <param name="SpreadDisponible">Disponibilní hours are spread too (typical plans of a základní škola have them).</param>
/// <param name="RemoveOthers">Other subjects of the filled grades and classes leave the plan.</param>
public record RvpFillOptions(bool Overwrite, bool CreateSubjects, bool SpreadDisponible, bool RemoveOthers);

public record FillCurriculumRequest(RvpFillOptions Options, bool DryRun);

/// <param name="SubjectsCreated">Made, or to be made.</param>
/// <param name="SubjectsMissing">Not made, their hours left out.</param>
/// <param name="ClassesSkipped">No program, or a grade not in it.</param>
public record RvpFillSummaryDto(
    int Added, int Overwritten, int Kept, int Removed, IReadOnlyList<string> SubjectsCreated, IReadOnlyList<string> SubjectsMissing,
    IReadOnlyList<string> ClassesSkipped, IReadOnlyList<string> Notes);

/// <param name="Overview">After the fill; null in a dry run.</param>
public record FillCurriculumResponse(RvpFillSummaryDto Summary, TeachingOverviewDto? Overview);
