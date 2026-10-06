using Haven.Web.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace Haven.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<UnitType> UnitTypes => Set<UnitType>();
    public DbSet<RentalApplication> Applications => Set<RentalApplication>();
    public DbSet<Residence> Residences => Set<Residence>();
    public DbSet<StatusEvent> StatusEvents => Set<StatusEvent>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<SeedRun> SeedRuns => Set<SeedRun>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Property>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Unit>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<RentalApplication>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Unit>().Property(x => x.MonthlyRent).HasPrecision(12, 2);
        b.Entity<Lease>().Property(x => x.MonthlyRent).HasPrecision(12, 2);
        b.Entity<UnitType>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Unit>().HasIndex(x => new { x.PropertyId, x.Number }).IsUnique();
        b.Entity<Unit>().HasOne(x => x.Property).WithMany(x => x.Units).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Unit>().HasOne(x => x.UnitType).WithMany().HasForeignKey(x => x.UnitTypeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RentalApplication>().HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RentalApplication>().HasOne(x => x.Applicant).WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RentalApplication>().HasIndex(x => x.SeedKey).IsUnique().HasFilter("[SeedKey] IS NOT NULL");
        b.Entity<RentalApplication>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Entity<RentalApplication>().HasIndex(x => new { x.ApplicantId, x.Status });
        b.Entity<RentalApplication>().HasIndex(x => new { x.UnitId, x.Status });
        b.Entity<StatusEvent>().Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
        b.Entity<StatusEvent>().Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
        b.Entity<StatusEvent>().Property(x => x.Outcome).HasConversion<string>().HasMaxLength(30);
        b.Entity<StatusEvent>().HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Lease>().HasOne(x => x.Unit).WithMany(x => x.Leases).HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Lease>().HasOne(x => x.RentalApplication).WithOne(x => x.Lease).HasForeignKey<Lease>(x => x.RentalApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Lease>().HasIndex(x => new { x.UnitId, x.StartDate, x.EndDate });
        b.Entity<Unit>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Unit_Rent", "[MonthlyRent] > 0");
            t.HasCheckConstraint("CK_Unit_Bedrooms", "[Bedrooms] >= 0 AND [Bedrooms] <= 20");
        });
        b.Entity<Lease>().ToTable(t => t.HasCheckConstraint("CK_Lease_Dates", "[EndDate] > [StartDate]"));
        b.Entity<Residence>().ToTable(t => t.HasCheckConstraint("CK_Residence_Dates", "[MoveOut] >= [MoveIn]"));
    }
}
