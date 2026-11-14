using Microsoft.EntityFrameworkCore;

namespace Soovioo.SupportPlans.Data;

public class PlansDbContext(DbContextOptions<PlansDbContext> options) : DbContext(options)
{
    public DbSet<SupportPlan> Plans => Set<SupportPlan>();
    public DbSet<SchoolPlan> SchoolPlans => Set<SchoolPlan>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SupportPlan>(plan =>
        {
            plan.Property(p => p.Code).HasMaxLength(32);
            plan.Property(p => p.Name).HasMaxLength(100);
            plan.HasIndex(p => p.Code).IsUnique();
            plan.ToTable(t => t.HasCheckConstraint("CK_Plans_ResponseWorkingDays", "\"ResponseWorkingDays\" >= 0"));
            plan.HasData(
                new SupportPlan { Id = 1, Code = "Free", Name = "Free", ResponseWorkingDays = 5 },
                new SupportPlan { Id = 2, Code = "Paid", Name = "Placený", ResponseWorkingDays = 2 },
                new SupportPlan { Id = 3, Code = "Priority", Name = "Prioritní", ResponseWorkingDays = 1 },
                new SupportPlan { Id = 4, Code = "Demo", Name = "Demo", ResponseWorkingDays = 5 },
                new SupportPlan { Id = 5, Code = "Pilot", Name = "Pilot", ResponseWorkingDays = 1 });
        });
        model.Entity<SchoolPlan>(assignment =>
        {
            assignment.HasOne(a => a.Plan).WithMany().OnDelete(DeleteBehavior.Restrict);
            assignment.HasIndex(a => new { a.SchoolId, a.ValidFrom });
            assignment.ToTable(t => t.HasCheckConstraint(
                "CK_SchoolPlans_Validity", "\"ValidTo\" IS NULL OR \"ValidTo\" >= \"ValidFrom\""));
        });
    }
}
