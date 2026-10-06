using Haven.Web.Data;
using Haven.Web.Domain;
using Haven.Web.Services;
using Haven.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
namespace Haven.Web.Controllers;

[Authorize]
public class PropertiesController(AppDbContext db, CatalogService catalog, BusinessClock clock) : Controller
{
    private async Task<CatalogPage> CatalogAsync()
    {
        var manager = User.IsInRole(Roles.Manager); var today = clock.Today;
        var buildings = await db.Properties.AsNoTracking().OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Address, p.Version }).ToListAsync();
        var units = await db.Units.AsNoTracking()
            .Where(u => manager || !u.Leases.Any(l => l.StartDate <= today && today < l.EndDate))
            .OrderBy(u => u.Number).Select(u => new UnitCard(u.Id, u.PropertyId, u.Property.Name,
                u.Property.Address, u.Number, u.Bedrooms, u.MonthlyRent, u.UnitType.Name, u.UnitType.IsActive,
                !u.Leases.Any(l => l.StartDate <= today && today < l.EndDate), u.Version)).ToListAsync();
        var grouped = units.ToLookup(u => u.PropertyId);
        var properties = buildings.Select(p => new PropertyCard(p.Id, p.Name, p.Address, p.Version, grouped[p.Id].ToList())).ToList();
        return new(properties, manager);
    }
    public async Task<IActionResult> Index() => View(await CatalogAsync());
    [HttpGet] public async Task<IActionResult> Catalog() => PartialView("_Catalog", await CatalogAsync());
    [Authorize(Roles = Roles.Manager), HttpGet]
    public async Task<IActionResult> Property(int id = 0)
    {
        if (id == 0) return PartialView("_PropertyModal", new PropertyInput());
        var p = await db.Properties.FindAsync(id); if (p == null) return NotFound();
        return PartialView("_PropertyModal", new PropertyInput { Id = p.Id, Name = p.Name, Address = p.Address, Version = p.Version });
    }
    [Authorize(Roles = Roles.Manager), HttpPost]
    public async Task<IActionResult> Property(PropertyInput input)
    {
        if (ModelState.IsValid) try { await catalog.SavePropertyAsync(input); return Json(new { success = true }); }
            catch (RuleException e) { ModelState.AddModelError(e.Field, e.Message); }
            catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "The property changed. Close this dialog and reload."); }
        return PartialView("_PropertyModal", input);
    }
    private async Task PopulateTypes(UnitInput input)
    {
        // An inactive type is offered only to the unit which already owns that value.
        var original = input.Id == 0 ? (int?)null : await db.Units.Where(u => u.Id == input.Id).Select(u => (int?)u.UnitTypeId).SingleOrDefaultAsync();
        input.Types = await db.UnitTypes.Where(t => t.IsActive || t.Id == original).OrderBy(t => t.Name)
            .Select(t => new SelectListItem(t.Name + (t.IsActive ? "" : " (inactive)"), t.Id.ToString())).ToListAsync();
    }
    [Authorize(Roles = Roles.Manager), HttpGet]
    public async Task<IActionResult> Unit(int propertyId, int id = 0)
    {
        var input = new UnitInput { PropertyId = propertyId };
        if (id != 0)
        {
            var u = await db.Units.FindAsync(id); if (u == null) return NotFound();
            input = new()
            {
                Id = id,
                PropertyId = u.PropertyId,
                Number = u.Number,
                Bedrooms = u.Bedrooms,
                MonthlyRent = u.MonthlyRent,
                UnitTypeId = u.UnitTypeId,
                Version = u.Version
            };
        }
        else if (!await db.Properties.AnyAsync(p => p.Id == propertyId)) return NotFound();
        await PopulateTypes(input); return PartialView("_UnitModal", input);
    }
    [Authorize(Roles = Roles.Manager), HttpPost]
    public async Task<IActionResult> Unit(UnitInput input)
    {
        if (ModelState.IsValid) try { await catalog.SaveUnitAsync(input); return Json(new { success = true }); }
            catch (RuleException e) { ModelState.AddModelError(e.Field, e.Message); }
            catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "The unit changed. Close this dialog and reload."); }
            catch (DbUpdateException) { ModelState.AddModelError("", "The unit could not be saved. Check for a duplicate unit number and reload."); }
        await PopulateTypes(input); return PartialView("_UnitModal", input);
    }
    [Authorize(Roles = Roles.Manager), HttpGet]
    public async Task<IActionResult> Delete(string kind, int id)
    {
        DeleteInput input;
        if (kind == "property") { var p = await db.Properties.FindAsync(id); if (p == null) return NotFound(); input = new() { Id = id, Kind = kind, Version = p.Version, Label = p.Name }; }
        else if (kind == "unit") { var u = await db.Units.FindAsync(id); if (u == null) return NotFound(); input = new() { Id = id, Kind = kind, Version = u.Version, Label = "Unit " + u.Number }; }
        else return BadRequest();
        return PartialView("_DeleteModal", input);
    }
    [Authorize(Roles = Roles.Manager), HttpPost]
    public async Task<IActionResult> Delete(DeleteInput input)
    {
        if (ModelState.IsValid) try { await catalog.DeleteAsync(input); return Json(new { success = true }); }
            catch (RuleException e) { ModelState.AddModelError(e.Field, e.Message); }
            catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "The record changed. Close this dialog and reload."); }
            catch (DbUpdateException) { ModelState.AddModelError("", "This record is in use and could not be removed."); }
        return PartialView("_DeleteModal", input);
    }
}
