using System.Security.Claims;
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
public class ApplicationsController(ApplicationService service, AppDbContext db, BusinessClock clock) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool Manager => User.IsInRole(Roles.Manager);
    public async Task<IActionResult> Index(ApplicationStatus? status, int? propertyId, string sort = "updated", int page = 1)
    {
        if (status != null && !Enum.IsDefined(status.Value)) return BadRequest();
        var query = service.VisibleTo(Actor, Manager).AsNoTracking();
        if (status != null) query = query.Where(a => a.Status == status);
        if (propertyId != null) query = query.Where(a => a.Unit.PropertyId == propertyId);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 12d)));
        sort = sort is "name" or "property" or "oldest" ? sort : "updated";
        var ordered = sort switch
        {
            "name" => query.OrderBy(a => a.Name).ThenBy(a => a.Id),
            "property" => query.OrderBy(a => a.Unit.Property.Name).ThenBy(a => a.Id),
            "oldest" => query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id),
            _ => query.OrderByDescending(a => a.UpdatedAt).ThenByDescending(a => a.Id)
        };
        var model = new ApplicationList
        {
            Status = status,
            PropertyId = propertyId,
            Sort = sort,
            Page = page,
            Total = total,
            Rows = await ordered.Skip((page - 1) * 12).Take(12).Select(a => new ApplicationRow(a.Id, a.Name,
                a.Unit.Property.Name, a.Unit.Number, a.Status, a.UpdatedAt)).ToListAsync(),
            Properties = await db.Properties.OrderBy(p => p.Name).Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToListAsync()
        };
        return View(model);
    }
    [Authorize(Roles = Roles.Applicant), HttpPost]
    public async Task<IActionResult> Create(int unitId)
    {
        try { var id = await service.CreateAsync(unitId, Actor); return RedirectToAction(nameof(Edit), new { id }); }
        catch (RuleException e) { TempData["Error"] = e.Message; return RedirectToAction("Index", "Properties"); }
    }
    [HttpGet]
    public async Task<IActionResult> Edit(int id, int section = 0)
    {
        var model = await service.PageAsync(id, Actor, Manager, section); return model == null ? NotFound() : View(model);
    }
    [Authorize(Roles = Roles.Applicant), HttpPost]
    public async Task<IActionResult> Edit(ApplicationPage input, string command)
    {
        var saved = await service.PageAsync(input.Id, Actor, false, input.Section); if (saved == null) return NotFound();
        if (!saved.Editable) return Forbid();
        if (input.Section is < 0 or > 2) return BadRequest();
        if (command == "back") return RedirectToAction(nameof(Edit), new { id = input.Id, section = Math.Max(0, input.Section - 1) });
        try
        {
            if (command == "continue" && input.Section == 0)
            {
                foreach (var error in ApplicationRules.ValidateInformation(input.Information))
                    foreach (var field in error.MemberNames.DefaultIfEmpty("")) ModelState.AddModelError("Information." + field, error.ErrorMessage!);
                if (ModelState.IsValid) await service.SaveInformationAsync(input.Id, Actor, input.Version, input.Information);
            }
            else if (command == "continue" && input.Section == 1)
            {
                if (ModelState.IsValid) await service.SaveHistoryAsync(input.Id, Actor, input.Version);
            }
            else if (command == "submit" && input.Section == 2)
            {
                if (ModelState.IsValid) { await service.SubmitAsync(input.Id, Actor, input.Version); TempData["Success"] = "Application submitted for review."; return RedirectToAction(nameof(Edit), new { id = input.Id, section = 2 }); }
            }
            else return BadRequest();
            if (ModelState.IsValid) return RedirectToAction(nameof(Edit), new { id = input.Id, section = input.Section + 1 });
        }
        catch (RuleException e) { ModelState.AddModelError(e.Field, e.Message); }
        catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "This application changed. Reload before trying again."); }
        // Preserve posted fields and the submitted version so a stale form cannot silently overwrite a newer save.
        if (input.Section == 0) saved.Information = input.Information;
        saved.Version = input.Version;
        return View(saved);
    }
    [Authorize(Roles = Roles.Applicant), HttpPost]
    public async Task<IActionResult> Withdraw(int id, Guid version)
    {
        if (await service.FindAsync(id, Actor, false) == null) return NotFound();
        try { await service.WithdrawAsync(id, Actor, version); TempData["Success"] = "Application withdrawn."; }
        catch (RuleException e) { TempData["Error"] = e.Message; }
        catch (DbUpdateConcurrencyException) { TempData["Error"] = "This application changed. Please reload."; }
        return RedirectToAction(nameof(Edit), new { id });
    }
    [Authorize(Roles = Roles.Applicant), HttpGet]
    public async Task<IActionResult> Residence(int applicationId, int id = 0)
    {
        var a = await service.FindAsync(applicationId, Actor, false); if (a == null) return NotFound();
        if (!ApplicationRules.Editable(a.Status)) return Forbid();
        var r = id == 0 ? null : a.Residences.SingleOrDefault(r => r.Id == id); if (id != 0 && r == null) return NotFound();
        return PartialView("_ResidenceModal", new ResidenceInput
        {
            Id = id,
            ApplicationId = a.Id,
            Version = a.Version,
            Address = r?.Address ?? "",
            LandlordName = r?.LandlordName ?? "",
            LandlordPhone = r?.LandlordPhone ?? "",
            MoveIn = r?.MoveIn,
            MoveOut = r?.MoveOut
        });
    }
    [Authorize(Roles = Roles.Applicant), HttpPost]
    public async Task<IActionResult> Residence(ResidenceInput input)
    {
        var a = await service.FindAsync(input.ApplicationId, Actor, false); if (a == null) return NotFound();
        if (!ApplicationRules.Editable(a.Status)) return Forbid();
        foreach (var error in ApplicationRules.ValidateResidence(input, clock.Today))
            foreach (var field in error.MemberNames.DefaultIfEmpty("")) if (!ModelState.TryGetValue(field, out var entry) || entry.Errors.Count == 0) ModelState.AddModelError(field, error.ErrorMessage!);
        if (ModelState.IsValid) try { await service.SaveResidenceAsync(input, Actor); return Json(new { success = true }); }
            catch (RuleException e) { ModelState.AddModelError(e.Field, e.Message); }
            catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "This application changed. Close this dialog and reload."); }
        return PartialView("_ResidenceModal", input);
    }
    [Authorize(Roles = Roles.Applicant), HttpGet]
    public async Task<IActionResult> RemoveResidence(int applicationId, int id)
    {
        var a = await service.FindAsync(applicationId, Actor, false); if (a == null) return NotFound();
        if (!ApplicationRules.Editable(a.Status)) return Forbid();
        var r = a.Residences.SingleOrDefault(r => r.Id == id); if (r == null) return NotFound();
        return PartialView("_RemoveResidenceModal", new ResidenceInput { Id = id, ApplicationId = applicationId, Version = a.Version, Address = r.Address });
    }
    [Authorize(Roles = Roles.Applicant), HttpPost]
    public async Task<IActionResult> RemoveResidence(int applicationId, int id, Guid version)
    {
        var a = await service.FindAsync(applicationId, Actor, false); if (a == null) return NotFound();
        if (!ApplicationRules.Editable(a.Status)) return Forbid();
        try { await service.RemoveResidenceAsync(applicationId, id, Actor, version); return Json(new { success = true }); }
        catch (RuleException e) { ModelState.AddModelError("", e.Message); }
        catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "This application changed. Close this dialog and reload."); }
        return PartialView("_RemoveResidenceModal", new ResidenceInput { Id = id, ApplicationId = applicationId, Version = version });
    }
    [Authorize(Roles = Roles.Manager), HttpGet]
    public async Task<IActionResult> Review(int applicationId)
    {
        var a = await service.FindAsync(applicationId, Actor, true); if (a == null) return NotFound();
        if (a.Status != ApplicationStatus.Submitted) return Forbid();
        return PartialView("_ReviewModal", new ReviewInput { ApplicationId = applicationId, Version = a.Version, StartDate = clock.Today });
    }
    [Authorize(Roles = Roles.Manager), HttpPost]
    public async Task<IActionResult> Review(ReviewInput input)
    {
        var a = await service.FindAsync(input.ApplicationId, Actor, true); if (a == null) return NotFound();
        if (a.Status != ApplicationStatus.Submitted) { ModelState.AddModelError("", "This application is no longer submitted. Close this dialog and reload."); }
        if (ModelState.IsValid) try { await service.ReviewAsync(input, Actor); return Json(new { success = true }); }
            catch (RuleException e) { ModelState.AddModelError(e.Field, e.Message); }
            catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "This application changed. Close this dialog and reload."); }
            catch (DbUpdateException) { ModelState.AddModelError("", "The review conflicted with another change. Close this dialog and reload."); }
        return PartialView("_ReviewModal", input);
    }
    [HttpGet]
    public async Task<IActionResult> Content(int id, int section = 0)
    {
        var model = await service.PageAsync(id, Actor, Manager, section); return model == null ? NotFound() : PartialView("_ApplicationContent", model);
    }
}
