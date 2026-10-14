using Microsoft.EntityFrameworkCore;
using Sova.Api.Grades;

namespace Sova.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Grade> Grades => Set<Grade>();
}
