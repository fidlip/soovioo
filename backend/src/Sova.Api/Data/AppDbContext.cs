using Microsoft.EntityFrameworkCore;
using Sova.Api.Attendance;
using Sova.Api.Evidence;
using Sova.Api.Grades;

namespace Sova.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Absence> Absences => Set<Absence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SchoolClass>().HasData(
            new SchoolClass { Id = 1, Name = "5.A" });

        modelBuilder.Entity<Student>().HasData(
            new Student { Id = 1, FirstName = "Jan", LastName = "Novák", SchoolClassId = 1 });

        modelBuilder.Entity<Subject>().HasData(
            new Subject { Id = 1, Name = "Matematika" },
            new Subject { Id = 2, Name = "Český jazyk" },
            new Subject { Id = 3, Name = "Anglický jazyk" });
    }
}
