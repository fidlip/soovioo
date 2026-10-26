using Soovioo.Api.Evidence;

namespace Soovioo.Api.Homework;

/// <summary>Join row narrowing a Homework from "whole class" to specific
/// students — see Homework.Students' doc comment. Composite key
/// (HomeworkId, StudentId), configured in AppDbContext.</summary>
public class HomeworkStudent
{
    public int HomeworkId { get; set; }
    public Homework? Homework { get; set; }

    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int SchoolId { get; set; }
}
