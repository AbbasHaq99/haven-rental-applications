using System.Data;
using System.ComponentModel.DataAnnotations;
using Haven.Web.Data;
using Haven.Web.Domain;
using Haven.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace Haven.Web.Services;

public class ApplicationService(AppDbContext db, BusinessClock clock)
{
    public IQueryable<RentalApplication> VisibleTo(string userId, bool manager) =>
        manager ? db.Applications : db.Applications.Where(x => x.ApplicantId == userId);
    public async Task<RentalApplication?> FindAsync(int id, string userId, bool manager) =>
        await VisibleTo(userId, manager).Include(x => x.Unit).ThenInclude(x => x.Property)
            .Include(x => x.Residences).Include(x => x.Lease).SingleOrDefaultAsync(x => x.Id == id);
    public async Task<ApplicationPage?> PageAsync(int id, string userId, bool manager, int section)
    {
        var a = await FindAsync(id, userId, manager);
        if (a == null) return null;
        return new ApplicationPage
        {
            Id = a.Id,
            Version = a.Version,
            Section = Math.Clamp(section, 0, 2),
            Status = a.Status,
            Editable = !manager && ApplicationRules.Editable(a.Status),
            InformationSaved = a.InformationSaved,
            HistorySaved = a.HistorySaved,
            Information = new() { Name = a.Name, Phone = a.Phone, Email = a.Email, CurrentAddress = a.CurrentAddress },
            Residences = a.Residences.OrderByDescending(r => r.MoveOut).ThenBy(r => r.Id).Select(r => new ResidenceInput
            {
                Id = r.Id,
                ApplicationId = a.Id,
                Version = a.Version,
                Address = r.Address,
                LandlordName = r.LandlordName,
                LandlordPhone = r.LandlordPhone,
                MoveIn = r.MoveIn,
                MoveOut = r.MoveOut
            }).ToList(),
            UnitLabel = $"{a.Unit.Property.Name} · Unit {a.Unit.Number}",
            MonthlyRent = a.Unit.MonthlyRent,
            Lease = a.Lease,
            Feedback = manager ? [] : await db.StatusEvents.AsNoTracking().Where(e => e.RentalApplicationId == id && e.Outcome != null)
                .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Select(e => new FeedbackItem(e.Outcome, e.At, e.Comment)).ToListAsync(),
            Events = manager ? await db.StatusEvents.AsNoTracking().Include(e => e.Actor)
                .Where(e => e.RentalApplicationId == id).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync() : []
        };
    }
    // SQL Server update locks serialize all lease decisions for the same unit, including the empty-lease case.
    private async Task<Unit?> LockUnitAsync(int id) => db.Database.IsSqlServer()
        // Read fresh values even if the controller already tracked this unit before the lock.
        ? await db.Units.FromSqlInterpolated($"SELECT * FROM [Units] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").AsNoTracking().SingleOrDefaultAsync()
        : await db.Units.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    private async Task RequireAvailableAsync(int unitId)
    {
        var today = clock.Today;
        if (await db.Leases.AnyAsync(l => l.UnitId == unitId && l.StartDate <= today && today < l.EndDate))
            throw new RuleException("This unit has an active lease and is no longer available.");
    }
    private static void RequireVersion(RentalApplication a, Guid version)
    {
        if (version != a.Version) throw new RuleException("This application changed. Reload the page before trying again.");
    }
    private void Touch(RentalApplication a) { a.Version = Guid.NewGuid(); a.UpdatedAt = clock.Now; }
    private void Transition(RentalApplication a, ApplicationStatus status, string actor, string? comment = null, ReviewOutcome? outcome = null)
    {
        a.Events.Add(new StatusEvent { FromStatus = a.Status, ToStatus = status, ActorId = actor, At = clock.Now, Comment = comment, Outcome = outcome });
        a.Status = status; Touch(a);
    }
    public async Task<int> CreateAsync(int unitId, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var unit = await LockUnitAsync(unitId) ?? throw new RuleException("The unit no longer exists.");
        await RequireAvailableAsync(unit.Id);
        var existing = await db.Applications.Where(a => a.UnitId == unitId && a.ApplicantId == userId &&
            (a.Status == ApplicationStatus.Draft || a.Status == ApplicationStatus.Submitted || a.Status == ApplicationStatus.Returned)).Select(a => (int?)a.Id).FirstOrDefaultAsync();
        if (existing != null) { await tx.CommitAsync(); return existing.Value; }
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var app = new RentalApplication
        {
            UnitId = unitId,
            ApplicantId = userId,
            Name = user.FullName,
            Email = user.Email ?? "",
            Phone = user.PhoneNumber ?? "",
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now
        };
        app.Events.Add(new() { ToStatus = ApplicationStatus.Draft, ActorId = userId, At = clock.Now, Comment = "Application created." });
        db.Applications.Add(app); await db.SaveChangesAsync(); await tx.CommitAsync(); return app.Id;
    }
    public async Task SaveInformationAsync(int id, string actor, Guid version, InformationInput input)
    {
        var a = await EditableAsync(id, actor, version);
        var errors = ApplicationRules.ValidateInformation(input).ToList();
        if (errors.Count != 0) throw new RuleException(errors[0].ErrorMessage!, "Information." + errors[0].MemberNames.FirstOrDefault());
        a.Name = input.Name.Trim(); a.Phone = input.Phone.Trim(); a.Email = input.Email.Trim(); a.CurrentAddress = input.CurrentAddress.Trim();
        a.InformationSaved = true; Touch(a); await db.SaveChangesAsync();
    }
    private async Task<RentalApplication> EditableAsync(int id, string actor, Guid version)
    {
        var a = await FindAsync(id, actor, false) ?? throw new RuleException("Application not found.");
        ApplicationRules.RequireEditable(a.Status); RequireVersion(a, version); return a;
    }
    public async Task SaveHistoryAsync(int id, string actor, Guid version)
    {
        var a = await EditableAsync(id, actor, version);
        RequireHistory(a); a.HistorySaved = true; Touch(a); await db.SaveChangesAsync();
    }
    private void RequireHistory(RentalApplication a)
    {
        if (a.Residences.Count == 0) throw new RuleException("Add at least one prior residence before continuing.");
        foreach (var r in a.Residences)
        {
            var input = new ResidenceInput
            {
                Address = r.Address,
                LandlordName = r.LandlordName,
                LandlordPhone = r.LandlordPhone,
                MoveIn = r.MoveIn,
                MoveOut = r.MoveOut
            };
            if (ApplicationRules.ValidateResidence(input, clock.Today).Any()) throw new RuleException("Correct your residence history before continuing.");
        }
    }
    public async Task SaveResidenceAsync(ResidenceInput input, string actor)
    {
        var a = await EditableAsync(input.ApplicationId, actor, input.Version);
        var errors = ApplicationRules.ValidateResidence(input, clock.Today).ToList();
        if (errors.Count != 0) throw new RuleException(errors[0].ErrorMessage!, errors[0].MemberNames.FirstOrDefault() ?? "");
        var residence = input.Id == 0 ? new Residence() : a.Residences.SingleOrDefault(r => r.Id == input.Id)
            ?? throw new RuleException("Residence not found.");
        if (input.Id == 0) a.Residences.Add(residence);
        residence.Address = input.Address.Trim(); residence.LandlordName = input.LandlordName.Trim();
        residence.LandlordPhone = input.LandlordPhone.Trim(); residence.MoveIn = input.MoveIn!.Value; residence.MoveOut = input.MoveOut!.Value;
        a.HistorySaved = false; Touch(a); await db.SaveChangesAsync();
    }
    public async Task RemoveResidenceAsync(int id, int residenceId, string actor, Guid version)
    {
        var a = await EditableAsync(id, actor, version);
        var r = a.Residences.SingleOrDefault(x => x.Id == residenceId) ?? throw new RuleException("Residence not found.");
        db.Residences.Remove(r); a.HistorySaved = false; Touch(a); await db.SaveChangesAsync();
    }
    public async Task SubmitAsync(int id, string actor, Guid version)
    {
        // Lock the unit first in all decisions to avoid inconsistent lock ordering.
        var unitId = await db.Applications.Where(a => a.Id == id && a.ApplicantId == actor).Select(a => (int?)a.UnitId).SingleOrDefaultAsync()
            ?? throw new RuleException("Application not found.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockUnitAsync(unitId);
        var a = await EditableAsync(id, actor, version);
        if (!a.InformationSaved || !a.HistorySaved) throw new RuleException("Save both sections before submitting.");
        if (ApplicationRules.ValidateInformation(new() { Name = a.Name, Email = a.Email, Phone = a.Phone, CurrentAddress = a.CurrentAddress }).Any())
            throw new RuleException("Correct your applicant information before submitting.");
        RequireHistory(a); await RequireAvailableAsync(a.UnitId);
        Transition(a, ApplicationStatus.Submitted, actor); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task WithdrawAsync(int id, string actor, Guid version)
    {
        var a = await FindAsync(id, actor, false) ?? throw new RuleException("Application not found.");
        RequireVersion(a, version);
        if (!ApplicationRules.Withdrawable(a.Status)) throw new RuleException("This application cannot be withdrawn.");
        Transition(a, ApplicationStatus.Withdrawn, actor); await db.SaveChangesAsync();
    }
    public async Task ReviewAsync(ReviewInput input, string actor)
    {
        var unitId = await db.Applications.Where(a => a.Id == input.ApplicationId).Select(a => (int?)a.UnitId).SingleOrDefaultAsync()
            ?? throw new RuleException("Application not found.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var unit = await LockUnitAsync(unitId) ?? throw new RuleException("Unit not found.");
        var a = await db.Applications.SingleAsync(a => a.Id == input.ApplicationId);
        RequireVersion(a, input.Version);
        if (input.Outcome == null) throw new RuleException("Choose an outcome.", "Outcome");
        ApplicationRules.RequireReview(a.Status, input.Outcome.Value, input.Comment);
        if (input.Outcome == ReviewOutcome.Approve)
        {
            await RequireAvailableAsync(a.UnitId);
            var start = input.StartDate ?? throw new RuleException("Choose a lease start date.", "StartDate");
            if (start < clock.Today) throw new RuleException("Lease start cannot be before today.", "StartDate");
            if (start > DateOnly.MaxValue.AddMonths(-12)) throw new RuleException("Choose a start date that allows a twelve-month term.", "StartDate");
            var end = start.AddMonths(12);
            if (await db.Leases.AnyAsync(l => l.UnitId == a.UnitId && l.StartDate < end && start < l.EndDate))
                throw new RuleException("The proposed term overlaps another lease.", "StartDate");
            db.Leases.Add(new() { UnitId = a.UnitId, RentalApplicationId = a.Id, StartDate = start, EndDate = end, MonthlyRent = unit.MonthlyRent });
        }
        var status = input.Outcome switch { ReviewOutcome.Approve => ApplicationStatus.Approved, ReviewOutcome.Return => ApplicationStatus.Returned, _ => ApplicationStatus.Denied };
        Transition(a, status, actor, input.Comment?.Trim(), input.Outcome);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
