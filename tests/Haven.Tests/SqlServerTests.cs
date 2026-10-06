using Haven.Web.Data;
using Haven.Web.Domain;
using Haven.Web.Services;
using Haven.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_TEST_SQL")))
            Skip = "Set HAVEN_TEST_SQL to run against a disposable SQL Server instance.";
    }
}
public class SqlServerTests : IAsyncLifetime
{
    private ServiceProvider services = null!;
    public async Task InitializeAsync()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HAVEN_TEST_SQL"))
        {
            InitialCatalog = "HavenTest_" + Guid.NewGuid().ToString("N")
        }.ConnectionString;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BusinessTimeZone"] = "America/Chicago",
            ["Seed:DemoPassword"] = "HavenDemo!2026"
        }).Build();
        var collection = new ServiceCollection();
        collection.AddLogging(); collection.AddSingleton<IConfiguration>(config);
        collection.AddSingleton<TimeProvider>(new FixedTime()); collection.AddSingleton<BusinessClock>();
        collection.AddDbContext<AppDbContext>(o => o.UseSqlServer(connection));
        collection.AddIdentityCore<AppUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>();
        collection.AddScoped<ApplicationService>();
        services = collection.BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var attempt = 0; ; attempt++)
        {
            try { await db.Database.MigrateAsync(); break; }
            catch (SqlException) when (attempt < 29) { await Task.Delay(TimeSpan.FromSeconds(2)); }
        }
        await Seeder.EnsureRolesAsync(scope.ServiceProvider);
        await Seeder.SeedAsync(scope.ServiceProvider);
    }
    public async Task DisposeAsync()
    {
        if (services == null) return;
        await using (var scope = services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        await services.DisposeAsync();
    }
    [SqlServerFact]
    public async Task MigrationAndSeedCanBeAppliedRepeatedlyWithoutChanges()
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(); await Seeder.SeedAsync(scope.ServiceProvider);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(12, await db.Units.CountAsync()); Assert.Equal(6, await db.Applications.CountAsync());
        Assert.Equal(6, await db.Applications.Select(a => a.Status).Distinct().CountAsync());
        Assert.Equal(2, await db.Roles.CountAsync()); Assert.Single(await db.Leases.ToListAsync());
        var applications = await scope.ServiceProvider.GetRequiredService<ApplicationService>().VisibleTo("none", true)
            .Where(a => a.Status == ApplicationStatus.Submitted && a.Unit.Property.Name == "Willow Park")
            .OrderByDescending(a => a.UpdatedAt).Skip(0).Take(12).ToListAsync();
        Assert.Single(applications);
    }
    [SqlServerFact]
    public async Task ParallelApprovalsForSameUnitCreateOnlyOneLease()
    {
        int firstId, secondId, unitId; Guid firstVersion, secondVersion; string manager;
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var service = scope.ServiceProvider.GetRequiredService<ApplicationService>();
            var first = await db.Applications.SingleAsync(a => a.Status == ApplicationStatus.Submitted);
            firstId = first.Id; firstVersion = first.Version; unitId = first.UnitId;
            manager = (await db.Users.SingleAsync(u => u.Email == "manager@haven.test")).Id;
            var owner = (await db.Users.SingleAsync(u => u.Email == "applicant2@haven.test")).Id;
            secondId = await service.CreateAsync(unitId, owner);
            var a = await db.Applications.SingleAsync(a => a.Id == secondId);
            await service.SaveInformationAsync(a.Id, owner, a.Version, new() { Name = "Another applicant", Email = "other@example.test", Phone = "312-555-0100", CurrentAddress = "1 Main St" });
            await service.SaveResidenceAsync(new() { ApplicationId = a.Id, Version = a.Version, Address = "2 Main St", LandlordName = "Sam", LandlordPhone = "312-555-0110", MoveIn = new(2024, 1, 1), MoveOut = new(2026, 1, 1) }, owner);
            await service.SaveHistoryAsync(a.Id, owner, a.Version); await service.SubmitAsync(a.Id, owner, a.Version); secondVersion = a.Version;
        }
        async Task<bool> Approve(int id, Guid version)
        {
            await using var scope = services.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<ApplicationService>().ReviewAsync(new ReviewInput
                {
                    ApplicationId = id,
                    Version = version,
                    Outcome = ReviewOutcome.Approve,
                    StartDate = new(2026, 10, 6)
                }, manager); return true;
            }
            catch (RuleException) { return false; }
        }
        var results = await Task.WhenAll(Approve(firstId, firstVersion), Approve(secondId, secondVersion));
        Assert.Single(results, x => x);
        await using var check = services.CreateAsyncScope(); var context = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await context.Leases.CountAsync(l => l.UnitId == unitId));
        Assert.Equal(1, await context.Applications.CountAsync(a => a.UnitId == unitId && a.Status == ApplicationStatus.Approved));
        Assert.Equal(1, await context.Applications.CountAsync(a => a.UnitId == unitId && a.Status == ApplicationStatus.Submitted));
    }
}
