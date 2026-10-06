using System.Security.Claims;
using Haven.Web.Data;
using Haven.Web.Domain;
using Haven.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Haven.Web.ViewComponents;

public class ApplicationStatsViewComponent(AppDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var id = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = db.Applications.AsNoTracking();
        if (!UserClaimsPrincipal.IsInRole(Roles.Manager)) query = query.Where(a => a.ApplicantId == id);
        var counts = await query.GroupBy(a => a.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count);
        return View(new ApplicationStats(counts.GetValueOrDefault(ApplicationStatus.Draft), counts.GetValueOrDefault(ApplicationStatus.Submitted),
            counts.GetValueOrDefault(ApplicationStatus.Returned), counts.GetValueOrDefault(ApplicationStatus.Approved)));
    }
}
