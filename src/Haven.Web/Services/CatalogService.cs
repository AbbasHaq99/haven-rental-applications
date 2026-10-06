using System.Data;
using Haven.Web.Data;
using Haven.Web.Domain;
using Haven.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace Haven.Web.Services;

public class CatalogService(AppDbContext db)
{
    private static void Version(Guid actual, Guid expected)
    {
        if (actual != expected) throw new RuleException("This record changed. Close this dialog and reload before trying again.");
    }
    public async Task SavePropertyAsync(PropertyInput input)
    {
        var p = input.Id == 0 ? new Property() : await db.Properties.FindAsync(input.Id) ?? throw new RuleException("Property not found.");
        if (input.Id != 0) Version(p.Version, input.Version); else db.Properties.Add(p);
        p.Name = input.Name.Trim(); p.Address = input.Address.Trim(); p.Version = Guid.NewGuid(); await db.SaveChangesAsync();
    }
    public async Task SaveUnitAsync(UnitInput input)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var u = input.Id == 0 ? new Unit { PropertyId = input.PropertyId } : await db.Units.FindAsync(input.Id) ?? throw new RuleException("Unit not found.");
        if (input.Id != 0) Version(u.Version, input.Version);
        if (u.PropertyId != input.PropertyId || !await db.Properties.AnyAsync(p => p.Id == input.PropertyId)) throw new RuleException("Property not found.");
        var type = await db.UnitTypes.FindAsync(input.UnitTypeId) ?? throw new RuleException("Choose a valid unit type.", "UnitTypeId");
        ApplicationRules.RequireType(type.IsActive, input.Id == 0 ? null : u.UnitTypeId, input.UnitTypeId);
        if (await db.Units.AnyAsync(x => x.PropertyId == input.PropertyId && x.Id != input.Id && x.Number == input.Number.Trim()))
            throw new RuleException("This property already has that unit number.", "Number");
        u.Number = input.Number.Trim(); u.Bedrooms = input.Bedrooms; u.MonthlyRent = input.MonthlyRent; u.UnitTypeId = input.UnitTypeId; u.Version = Guid.NewGuid();
        if (input.Id == 0) db.Units.Add(u);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task DeleteAsync(DeleteInput input)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (input.Kind == "property")
        {
            var p = await db.Properties.Include(p => p.Units).SingleOrDefaultAsync(p => p.Id == input.Id) ?? throw new RuleException("Property not found.");
            Version(p.Version, input.Version);
            if (await db.Applications.AnyAsync(a => a.Unit.PropertyId == input.Id) || await db.Leases.AnyAsync(l => l.Unit.PropertyId == input.Id))
                throw new RuleException("This property has application or lease history and cannot be removed.");
            db.Units.RemoveRange(p.Units); db.Properties.Remove(p);
        }
        else if (input.Kind == "unit")
        {
            var u = await db.Units.FindAsync(input.Id) ?? throw new RuleException("Unit not found.");
            Version(u.Version, input.Version);
            if (await db.Applications.AnyAsync(a => a.UnitId == input.Id) || await db.Leases.AnyAsync(l => l.UnitId == input.Id))
                throw new RuleException("This unit has application or lease history and cannot be removed.");
            db.Units.Remove(u);
        }
        else throw new RuleException("Invalid removal request.");
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
