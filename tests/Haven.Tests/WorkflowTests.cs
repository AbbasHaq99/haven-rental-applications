using Haven.Web.Data;
using Haven.Web.Domain;
using Haven.Web.Services;
using Haven.Web.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace Haven.Tests;

public class WorkflowTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private AppDbContext db = null!;
    private ApplicationService service = null!;
    private BusinessClock clock = null!;
    private int unitId;
    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new TestDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        clock = new(new FixedTime(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["BusinessTimeZone"] = "America/Chicago" }).Build());
        service = new(db, clock);
        db.Users.AddRange(new AppUser { Id = "owner", UserName = "owner", FullName = "Alex", Email = "alex@example.test" }, new AppUser { Id = "other", UserName = "other" }, new AppUser { Id = "manager", UserName = "manager" });
        var p = new Property { Name = "Park", Address = "1 Main St" }; var t = new UnitType { Name = "Apartment" };
        var unit = new Unit { Property = p, UnitType = t, Number = "101", MonthlyRent = 1600m, Bedrooms = 2 };
        db.Units.Add(unit); await db.SaveChangesAsync(); unitId = unit.Id;
    }
    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    private async Task<RentalApplication> Draft(string owner = "owner")
    {
        var id = await service.CreateAsync(unitId, owner);
        return await db.Applications.Include(a => a.Residences).SingleAsync(a => a.Id == id);
    }
    private async Task<RentalApplication> Submitted(string owner = "owner")
    {
        var a = await Draft(owner);
        await service.SaveInformationAsync(a.Id, owner, a.Version, new() { Name = "Alex", Phone = "312-555-0100", Email = "alex@example.test", CurrentAddress = "1 Main St" });
        await service.SaveResidenceAsync(new() { ApplicationId = a.Id, Version = a.Version, Address = "2 Main St", LandlordName = "Sam", LandlordPhone = "312-555-0110", MoveIn = clock.Today.AddYears(-2), MoveOut = clock.Today.AddMonths(-1) }, owner);
        await service.SaveHistoryAsync(a.Id, owner, a.Version);
        await service.SubmitAsync(a.Id, owner, a.Version); return a;
    }
    [Fact]
    public async Task CreateReusesAnExistingOpenApplication()
    {
        var a = await Draft(); Assert.Equal(a.Id, await service.CreateAsync(unitId, "owner")); Assert.Equal(1, await db.Applications.CountAsync());
    }
    [Fact]
    public async Task InvalidInformationDoesNotPersistOrMarkSectionSaved()
    {
        var a = await Draft();
        await Assert.ThrowsAsync<RuleException>(() => service.SaveInformationAsync(a.Id, "owner", a.Version, new() { Name = "Changed" }));
        Assert.Equal("Alex", a.Name); Assert.False(a.InformationSaved);
    }
    [Fact]
    public async Task CannotSubmitUntilBothSectionsAreSaved()
    {
        var a = await Draft(); await Assert.ThrowsAsync<RuleException>(() => service.SubmitAsync(a.Id, "owner", a.Version));
        Assert.Equal(ApplicationStatus.Draft, a.Status);
    }
    [Fact]
    public async Task EmptyHistoryCannotBeMarkedComplete()
    {
        var a = await Draft(); await Assert.ThrowsAsync<RuleException>(() => service.SaveHistoryAsync(a.Id, "owner", a.Version)); Assert.False(a.HistorySaved);
    }
    [Fact]
    public async Task AnotherApplicantCannotReadOrSaveApplication()
    {
        var a = await Draft(); Assert.Null(await service.PageAsync(a.Id, "other", false, 0));
        await Assert.ThrowsAsync<RuleException>(() => service.SaveInformationAsync(a.Id, "other", a.Version, new()));
    }
    [Fact]
    public async Task StaleSaveIsRejectedWithoutOverwriting()
    {
        var a = await Draft(); var old = a.Version;
        await service.SaveInformationAsync(a.Id, "owner", old, new() { Name = "First", Phone = "312-555-0100", Email = "first@example.test", CurrentAddress = "1 Main St" });
        await Assert.ThrowsAsync<RuleException>(() => service.SaveInformationAsync(a.Id, "owner", old, new() { Name = "Second" })); Assert.Equal("First", a.Name);
    }
    [Fact]
    public async Task OptimisticTokenRejectsAConcurrentContextSave()
    {
        var a = await Draft();
        await using var second = new TestDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var stale = await second.Applications.SingleAsync(x => x.Id == a.Id);
        await service.SaveInformationAsync(a.Id, "owner", a.Version, new() { Name = "First", Phone = "312-555-0100", Email = "first@example.test", CurrentAddress = "1 Main St" });
        stale.Name = "Second"; stale.Version = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
    [Fact]
    public async Task SubmittedApplicationRejectsInformationAndResidenceChanges()
    {
        var a = await Submitted();
        await Assert.ThrowsAsync<RuleException>(() => service.SaveInformationAsync(a.Id, "owner", a.Version, new()));
        await Assert.ThrowsAsync<RuleException>(() => service.SaveResidenceAsync(new() { ApplicationId = a.Id, Version = a.Version }, "owner"));
        await Assert.ThrowsAsync<RuleException>(() => service.RemoveResidenceAsync(a.Id, a.Residences[0].Id, "owner", a.Version));
    }
    [Fact]
    public async Task ReturnedApplicationCanBeCorrectedAndResubmitted()
    {
        var a = await Submitted(); await service.ReviewAsync(new() { ApplicationId = a.Id, Version = a.Version, Outcome = ReviewOutcome.Return, Comment = "Confirm phone" }, "manager");
        await service.SaveInformationAsync(a.Id, "owner", a.Version, new() { Name = "Alex", Phone = "312-555-0199", Email = "alex@example.test", CurrentAddress = "1 Main St" });
        await service.SubmitAsync(a.Id, "owner", a.Version); Assert.Equal(ApplicationStatus.Submitted, a.Status);
        Assert.Contains(await db.StatusEvents.ToListAsync(), e => e.Outcome == ReviewOutcome.Return && e.Comment == "Confirm phone");
    }
    [Fact]
    public async Task ApprovalCreatesExactlyTwelveMonthsAndPreservesCompetingApplications()
    {
        var a = await Submitted(); var other = await Submitted("other");
        await service.ReviewAsync(new() { ApplicationId = a.Id, Version = a.Version, Outcome = ReviewOutcome.Approve, StartDate = clock.Today }, "manager");
        var lease = await db.Leases.SingleAsync(); Assert.Equal(clock.Today.AddMonths(12), lease.EndDate);
        Assert.Equal(1600m, lease.MonthlyRent); Assert.Equal(ApplicationStatus.Submitted, other.Status);
        await Assert.ThrowsAsync<RuleException>(() => service.ReviewAsync(new() { ApplicationId = other.Id, Version = other.Version, Outcome = ReviewOutcome.Approve, StartDate = clock.Today }, "manager"));
        Assert.Equal(1, await db.Leases.CountAsync());
    }
    [Fact]
    public async Task ApprovalSnapshotsCurrentRentWhenTheUnitWasAlreadyTracked()
    {
        var a = await Submitted();
        await service.FindAsync(a.Id, "manager", true);
        await using (var second = new TestDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options))
        {
            var unit = await second.Units.SingleAsync(u => u.Id == unitId);
            unit.MonthlyRent = 1875.50m; unit.Version = Guid.NewGuid();
            await second.SaveChangesAsync();
        }
        await service.ReviewAsync(new() { ApplicationId = a.Id, Version = a.Version, Outcome = ReviewOutcome.Approve, StartDate = clock.Today }, "manager");
        Assert.Equal(1875.50m, (await db.Leases.AsNoTracking().SingleAsync()).MonthlyRent);
    }
    [Fact]
    public async Task SubmissionIsRejectedWhenACompetingApplicationWasApproved()
    {
        var a = await Submitted(); var other = await Draft("other");
        other.Name = "Other"; other.Phone = "312-555-0100"; other.Email = "other@example.test"; other.CurrentAddress = "3 Main St"; other.InformationSaved = true; other.HistorySaved = true;
        other.Residences.Add(new() { Address = "4 Main St", LandlordName = "Sam", LandlordPhone = "312-555-0100", MoveIn = clock.Today.AddYears(-1), MoveOut = clock.Today.AddDays(-1) }); await db.SaveChangesAsync();
        await service.ReviewAsync(new() { ApplicationId = a.Id, Version = a.Version, Outcome = ReviewOutcome.Approve, StartDate = clock.Today }, "manager");
        await Assert.ThrowsAsync<RuleException>(() => service.SubmitAsync(other.Id, "other", other.Version)); Assert.Equal(ApplicationStatus.Draft, other.Status);
    }
    [Fact]
    public async Task FutureLeaseOverlapIsRejectedEvenWhenUnitIsAvailableToday()
    {
        var a = await Submitted(); var other = await Submitted("other");
        await service.ReviewAsync(new() { ApplicationId = a.Id, Version = a.Version, Outcome = ReviewOutcome.Approve, StartDate = clock.Today.AddMonths(1) }, "manager");
        await Assert.ThrowsAsync<RuleException>(() => service.ReviewAsync(new() { ApplicationId = other.Id, Version = other.Version, Outcome = ReviewOutcome.Approve, StartDate = clock.Today }, "manager"));
        Assert.Equal(1, await db.Leases.CountAsync());
    }
    [Fact]
    public async Task ResidenceChangeInvalidatesSavedHistory()
    {
        var a = await Draft(); await service.SaveResidenceAsync(new() { ApplicationId = a.Id, Version = a.Version, Address = "2 Main St", LandlordName = "Sam", LandlordPhone = "312-555-0100", MoveIn = clock.Today.AddYears(-2), MoveOut = clock.Today.AddMonths(-1) }, "owner");
        await service.SaveHistoryAsync(a.Id, "owner", a.Version); Assert.True(a.HistorySaved);
        await service.RemoveResidenceAsync(a.Id, a.Residences[0].Id, "owner", a.Version); Assert.False(a.HistorySaved);
    }
    [Theory]
    [InlineData(ReviewOutcome.Return)]
    [InlineData(ReviewOutcome.Deny)]
    public async Task ReviewRequiresCommentAndPersistsNoOutcomeOnFailure(ReviewOutcome outcome)
    {
        var a = await Submitted(); await Assert.ThrowsAsync<RuleException>(() => service.ReviewAsync(new() { ApplicationId = a.Id, Version = a.Version, Outcome = outcome, Comment = " " }, "manager"));
        Assert.Equal(ApplicationStatus.Submitted, a.Status); Assert.Empty(await db.Leases.ToListAsync());
    }
    [Fact]
    public async Task WithdrawnIsTerminal()
    {
        var a = await Draft(); await service.WithdrawAsync(a.Id, "owner", a.Version);
        await Assert.ThrowsAsync<RuleException>(() => service.WithdrawAsync(a.Id, "owner", a.Version));
        await Assert.ThrowsAsync<RuleException>(() => service.SubmitAsync(a.Id, "owner", a.Version));
    }
    [Fact]
    public async Task ApplicantsReceiveFeedbackWithoutManagerHistory()
    {
        var a = await Submitted(); await service.ReviewAsync(new() { ApplicationId = a.Id, Version = a.Version, Outcome = ReviewOutcome.Return, Comment = "Please correct phone" }, "manager");
        var applicant = await service.PageAsync(a.Id, "owner", false, 2); Assert.Empty(applicant!.Events); Assert.Single(applicant.Feedback);
        var manager = await service.PageAsync(a.Id, "manager", true, 2); Assert.NotEmpty(manager!.Events); Assert.False(manager.Editable);
    }
    [Fact]
    public async Task InactiveTypeAssignmentIsEnforcedAgainstThePersistedUnit()
    {
        var catalog = new CatalogService(db); var unit = await db.Units.FindAsync(unitId);
        var inactive = new UnitType { Name = "Legacy", IsActive = false }; db.UnitTypes.Add(inactive); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<RuleException>(() => catalog.SaveUnitAsync(new() { PropertyId = unit!.PropertyId, Number = "202", Bedrooms = 1, MonthlyRent = 1500, UnitTypeId = inactive.Id }));
        await Assert.ThrowsAsync<RuleException>(() => catalog.SaveUnitAsync(new() { Id = unit!.Id, Version = unit.Version, PropertyId = unit.PropertyId, Number = unit.Number, Bedrooms = 1, MonthlyRent = 1500, UnitTypeId = inactive.Id }));
        unit!.UnitTypeId = inactive.Id; await db.SaveChangesAsync();
        await catalog.SaveUnitAsync(new() { Id = unit.Id, Version = unit.Version, PropertyId = unit.PropertyId, Number = unit.Number, Bedrooms = 1, MonthlyRent = 1750, UnitTypeId = inactive.Id });
        Assert.Equal(1750, unit.MonthlyRent); Assert.Equal(inactive.Id, unit.UnitTypeId);
    }
    [Fact]
    public async Task HistoricPropertiesAndUnitsCannotBeRemoved()
    {
        var a = await Draft(); var unit = await db.Units.FindAsync(unitId); var property = await db.Properties.FindAsync(unit!.PropertyId); var catalog = new CatalogService(db);
        await Assert.ThrowsAsync<RuleException>(() => catalog.DeleteAsync(new() { Id = unit.Id, Version = unit.Version, Kind = "unit" }));
        await Assert.ThrowsAsync<RuleException>(() => catalog.DeleteAsync(new() { Id = property!.Id, Version = property.Version, Kind = "property" }));
        Assert.NotNull(await db.Applications.FindAsync(a.Id));
    }
    [Fact]
    public async Task EmptyPropertyCanBeRemovedTogetherWithItsUnits()
    {
        var unit = await db.Units.FindAsync(unitId); var property = await db.Properties.FindAsync(unit!.PropertyId);
        await new CatalogService(db).DeleteAsync(new() { Id = property!.Id, Version = property.Version, Kind = "property" });
        Assert.Empty(await db.Properties.ToListAsync()); Assert.Empty(await db.Units.ToListAsync());
    }

}
public class FixedTime : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 16, 0, 0, TimeSpan.Zero);
}
