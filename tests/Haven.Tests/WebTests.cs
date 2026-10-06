using System.Net;
using System.Text.RegularExpressions;
using Haven.Web.Data;
using Haven.Web.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Tests;

public class WebTests : IDisposable
{
    private readonly WebFactory factory = new();
    private readonly HttpClient client;
    public WebTests() { client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }); }
    public void Dispose() { client.Dispose(); factory.Dispose(); }
    private static string Field(string html, string name)
    {
        var tag = Regex.Matches(html, "<input[^>]+>").Select(m => m.Value).First(t => t.Contains("name=\"" + name + "\""));
        return WebUtility.HtmlDecode(Regex.Match(tag, "value=\"([^\"]*)\"").Groups[1].Value);
    }
    private async Task<string> Get(string path)
    {
        var response = await client.GetAsync(path); var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body); return body;
    }
    private async Task Login(string email = "applicant@haven.test")
    {
        var html = await Get("/Account/Login");
        var result = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = "HavenDemo!2026",
            ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken")
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }
    [Fact]
    public async Task AnonymousAccessRedirectsToLogin()
    {
        var response = await client.GetAsync("/Applications"); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }
    [Fact]
    public async Task PostsRequireAntiforgeryToken()
    {
        await Login(); var response = await client.PostAsync("/Applications/Create", new FormUrlEncodedContent(new Dictionary<string, string> { { "unitId", "1" } })); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task ApplicantCannotOpenManagerModalAndCannotSeeOtherApplicantsData()
    {
        await Login(); Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Properties/Property")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var other = await db.Users.SingleAsync(u => u.Email == "applicant2@haven.test"); var unit = await db.Units.FirstAsync();
        var a = new RentalApplication { ApplicantId = other.Id, UnitId = unit.Id, Name = "SECRET OTHER APPLICANT", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.Applications.Add(a); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Applications/Edit/{a.Id}")).StatusCode);
        var list = await Get("/Applications"); Assert.DoesNotContain("SECRET OTHER APPLICANT", list);
    }
    [Fact]
    public async Task InvalidModalFormReturnsTheSamePartialWithFieldErrors()
    {
        await Login("manager@haven.test"); var html = await Get("/Properties/Property");
        var response = await client.PostAsync("/Properties/Property", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "",
            ["Address"] = "",
            ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken")
        }));
        var body = await response.Content.ReadAsStringAsync(); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-modal-form", body); Assert.Contains("The Name field is required", body); Assert.DoesNotContain("<!DOCTYPE", body);
    }
    [Fact]
    public async Task ValidModalSaveReturnsSuccessAndCatalogIncludesNewProperty()
    {
        await Login("manager@haven.test"); var html = await Get("/Properties/Property");
        var response = await client.PostAsync("/Properties/Property", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "New Place",
            ["Address"] = "10 Example St",
            ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken")
        }));
        Assert.Contains("\"success\":true", await response.Content.ReadAsStringAsync()); Assert.Contains("New Place", await Get("/Properties/Catalog"));
    }
    [Fact]
    public async Task BackDoesNotSavePostedInformation()
    {
        await Login(); using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var id = await db.Applications.Where(a => a.Status == ApplicationStatus.Draft).Select(a => a.Id).SingleAsync();
        var html = await Get($"/Applications/Edit/{id}?section=1");
        var response = await client.PostAsync("/Applications/Edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = id.ToString(),
            ["Section"] = "1",
            ["Version"] = Field(html, "Version"),
            ["command"] = "back",
            ["Information.Name"] = "UNSAVED CHANGE",
            ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken")
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); db.ChangeTracker.Clear(); Assert.NotEqual("UNSAVED CHANGE", (await db.Applications.FindAsync(id))!.Name);
    }
    [Fact]
    public async Task SubmitCannotBeForgedFromAnEarlierSection()
    {
        await Login(); using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var id = await db.Applications.Where(a => a.Status == ApplicationStatus.Draft).Select(a => a.Id).SingleAsync();
        var html = await Get($"/Applications/Edit/{id}");
        var response = await client.PostAsync("/Applications/Edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = id.ToString(),
            ["Section"] = "0",
            ["Version"] = Field(html, "Version"),
            ["command"] = "submit",
            ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken")
        })); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task SeedPreservesRenamedAndDeletedCatalogRecords()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var property = await db.Properties.SingleAsync(p => p.Name == "Willow Park");
        property.Name = "Renamed Park";
        var unit = await db.Units.SingleAsync(u => u.PropertyId == property.Id && u.Number == "101");
        unit.Number = "A1";
        var deleted = await db.Properties.Include(p => p.Units).SingleAsync(p => p.Name == "Oak & River");
        db.Units.RemoveRange(deleted.Units); db.Properties.Remove(deleted);
        await db.SaveChangesAsync();
        await Seeder.SeedAsync(scope.ServiceProvider);
        Assert.Equal(2, await db.Properties.CountAsync()); Assert.Equal(8, await db.Units.CountAsync());
        Assert.False(await db.Properties.AnyAsync(p => p.Name == "Willow Park" || p.Name == "Oak & River"));
        Assert.False(await db.Units.AnyAsync(u => u.PropertyId == property.Id && u.Number == "101"));
    }
    [Fact]
    public async Task SeedAdoptsLegacyDatabaseWithoutRecreatingRenamedProperties()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SeedRuns.RemoveRange(await db.SeedRuns.ToListAsync());
        (await db.Properties.SingleAsync(p => p.Name == "Willow Park")).Name = "Renamed Park";
        await db.SaveChangesAsync();
        await Seeder.SeedAsync(scope.ServiceProvider);
        Assert.Equal(3, await db.Properties.CountAsync()); Assert.Equal(12, await db.Units.CountAsync());
        Assert.False(await db.Properties.AnyAsync(p => p.Name == "Willow Park"));
        Assert.Single(await db.SeedRuns.ToListAsync());
    }
    [Fact]
    public async Task SeedIsIdempotentAndIncludesEveryStatus()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var applications = await db.Applications.CountAsync(); var units = await db.Units.CountAsync(); var events = await db.StatusEvents.CountAsync();
        await Seeder.SeedAsync(scope.ServiceProvider);
        Assert.Equal(applications, await db.Applications.CountAsync()); Assert.Equal(units, await db.Units.CountAsync()); Assert.Equal(events, await db.StatusEvents.CountAsync());
        Assert.Equal(Enum.GetValues<ApplicationStatus>().Length, await db.Applications.Select(a => a.Status).Distinct().CountAsync());
        Assert.Contains(await db.UnitTypes.ToListAsync(), t => !t.IsActive);
    }
}
