using Bogus;
using Haven.Web.Domain;
using Haven.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Haven.Web.Data;

public static class Seeder
{
    public static async Task EnsureRolesAsync(IServiceProvider services)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var name in new[] { Roles.Applicant, Roles.Manager })
        {
            if (await roles.RoleExistsAsync(name)) continue;
            var result = await roles.CreateAsync(new(name));
            if (!result.Succeeded && !await roles.RoleExistsAsync(name)) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var clock = services.GetRequiredService<BusinessClock>();
        var config = services.GetRequiredService<IConfiguration>();
        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Set Seed:DemoPassword before enabling demo seeding.");
        // One transaction makes a partially interrupted seed safe to retry. The SQL application lock also protects parallel startups.
        await using var tx = await db.Database.BeginTransactionAsync();
        if (db.Database.IsSqlServer()) await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sp_getapplock @Resource = 'HavenDemoSeed', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000; IF @result < 0 THROW 51000, 'Could not acquire seed lock', 1;");
        const string seedId = "demo-v1";
        if (await db.SeedRuns.AnyAsync(s => s.Id == seedId)) { await tx.CommitAsync(); return; }
        // Adopt databases seeded before the completion marker was introduced, preserving catalog edits.
        var legacyKeys = Enum.GetValues<ApplicationStatus>().Select(s => "demo-v1-" + s).ToArray();
        if (await db.Applications.CountAsync(a => legacyKeys.Contains(a.SeedKey!)) == legacyKeys.Length)
        {
            db.SeedRuns.Add(new() { Id = seedId });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return;
        }
        var fake = new Faker("en_US") { Random = new Randomizer(2026) };
        var people = new List<AppUser>();
        foreach (var (email, role) in new[] {
            ("manager@haven.test", Roles.Manager), ("manager2@haven.test", Roles.Manager),
            ("applicant@haven.test", Roles.Applicant), ("applicant2@haven.test", Roles.Applicant), ("applicant3@haven.test", Roles.Applicant)
        })
        {
            var name = fake.Name.FullName();
            var user = await users.FindByEmailAsync(email);
            if (user == null)
            {
                user = new() { UserName = email, Email = email, EmailConfirmed = true, FullName = name, PhoneNumber = fake.Phone.PhoneNumber("###-###-####") };
                var result = await users.CreateAsync(user, password);
                if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }
            if (!await users.IsInRoleAsync(user, role))
            {
                var result = await users.AddToRoleAsync(user, role);
                if (!result.Succeeded) throw new InvalidOperationException("Could not seed user role.");
            }
            people.Add(user);
        }
        var types = new List<UnitType>();
        foreach (var (name, active) in new[] { ("Apartment", true), ("Loft", true), ("Townhome", true), ("Legacy studio", false) })
        {
            var type = await db.UnitTypes.SingleOrDefaultAsync(t => t.Name == name);
            if (type == null) { type = new() { Name = name, IsActive = active }; db.UnitTypes.Add(type); await db.SaveChangesAsync(); }
            types.Add(type);
        }
        var units = new List<Unit>();
        foreach (var name in new[] { "Willow Park", "The Linden", "Oak & River" })
        {
            var property = await db.Properties.SingleOrDefaultAsync(p => p.Name == name);
            var address = fake.Address.StreetAddress() + ", Chicago, IL " + fake.Address.ZipCode("606##");
            if (property == null) { property = new() { Name = name, Address = address }; db.Properties.Add(property); await db.SaveChangesAsync(); }
            for (int n = 0; n < 4; n++)
            {
                var number = (101 + n).ToString();
                var unit = await db.Units.SingleOrDefaultAsync(u => u.PropertyId == property.Id && u.Number == number);
                var rent = fake.Random.Int(14, 28) * 100m;
                if (unit == null)
                {
                    unit = new()
                    {
                        PropertyId = property.Id,
                        Number = number,
                        Bedrooms = n % 3,
                        MonthlyRent = rent,
                        UnitTypeId = types[n].Id
                    }; db.Units.Add(unit); await db.SaveChangesAsync();
                }
                units.Add(unit);
            }
        }
        var manager = people[0]; var applicant = people[2];
        var index = 0;
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            var key = "demo-v1-" + status;
            if (await db.Applications.AnyAsync(a => a.SeedKey == key)) { index++; continue; }
            var a = new RentalApplication
            {
                SeedKey = key,
                UnitId = units[index++].Id,
                ApplicantId = applicant.Id,
                Name = applicant.FullName,
                Phone = applicant.PhoneNumber!,
                Email = applicant.Email!,
                CurrentAddress = fake.Address.FullAddress(),
                InformationSaved = true,
                HistorySaved = true,
                CreatedAt = clock.Now.AddDays(-6),
                UpdatedAt = clock.Now.AddDays(-1)
            };
            a.Residences.Add(new()
            {
                Address = fake.Address.FullAddress(),
                LandlordName = fake.Name.FullName(),
                LandlordPhone = fake.Phone.PhoneNumber("###-###-####"),
                MoveIn = clock.Today.AddYears(-3),
                MoveOut = clock.Today.AddMonths(-1)
            });
            a.Events.Add(new() { ToStatus = ApplicationStatus.Draft, ActorId = applicant.Id, At = a.CreatedAt, Comment = "Application created." });
            if (status is not (ApplicationStatus.Draft or ApplicationStatus.Withdrawn))
            {
                a.Events.Add(new() { FromStatus = ApplicationStatus.Draft, ToStatus = ApplicationStatus.Submitted, ActorId = applicant.Id, At = clock.Now.AddDays(-3) });
            }
            if (status is ApplicationStatus.Approved or ApplicationStatus.Returned or ApplicationStatus.Denied)
            {
                var outcome = status == ApplicationStatus.Approved ? ReviewOutcome.Approve : status == ApplicationStatus.Returned ? ReviewOutcome.Return : ReviewOutcome.Deny;
                a.Events.Add(new()
                {
                    FromStatus = ApplicationStatus.Submitted,
                    ToStatus = status,
                    ActorId = manager.Id,
                    At = clock.Now.AddDays(-1),
                    Outcome = outcome,
                    Comment = outcome == ReviewOutcome.Return ? "Please confirm your current phone number and residence dates." : outcome == ReviewOutcome.Deny ? "The application does not meet our rental requirements." : "Approved. Welcome to Haven."
                });
            }
            if (status == ApplicationStatus.Withdrawn) a.Events.Add(new() { FromStatus = ApplicationStatus.Draft, ToStatus = status, ActorId = applicant.Id, At = clock.Now.AddDays(-1) });
            a.Status = status;
            if (status == ApplicationStatus.Approved) a.Lease = new() { UnitId = a.UnitId, StartDate = clock.Today.AddDays(-10), EndDate = clock.Today.AddDays(-10).AddMonths(12), MonthlyRent = units[index - 1].MonthlyRent };
            db.Applications.Add(a); await db.SaveChangesAsync();
        }
        db.SeedRuns.Add(new() { Id = seedId });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
