using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
namespace Haven.Web.Domain;

public static class Roles { public const string Applicant = "Applicant"; public const string Manager = "PropertyManager"; }
public enum ApplicationStatus { Draft, Submitted, Returned, Approved, Denied, Withdrawn }
public enum ReviewOutcome { Approve, Return, Deny }
public class SeedRun
{
    [MaxLength(80)] public string Id { get; set; } = "";
}
public class AppUser : IdentityUser { [MaxLength(120)] public string FullName { get; set; } = ""; }
public class Property
{
    public int Id { get; set; }
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(300)] public string Address { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<Unit> Units { get; set; } = [];
}
public class UnitType
{
    public int Id { get; set; }
    [MaxLength(60)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public class Unit
{
    public int Id { get; set; }
    public int PropertyId { get; set; }
    public Property Property { get; set; } = null!;
    [MaxLength(30)] public string Number { get; set; } = "";
    public int Bedrooms { get; set; }
    public decimal MonthlyRent { get; set; }
    public int UnitTypeId { get; set; }
    public UnitType UnitType { get; set; } = null!;
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<Lease> Leases { get; set; } = [];
}
public class RentalApplication
{
    [MaxLength(80)] public string? SeedKey { get; set; }
    public int Id { get; set; }
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public string ApplicantId { get; set; } = "";
    public AppUser Applicant { get; set; } = null!;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Draft;
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(40)] public string Phone { get; set; } = "";
    [MaxLength(256)] public string Email { get; set; } = "";
    [MaxLength(300)] public string CurrentAddress { get; set; } = "";
    public bool InformationSaved { get; set; }
    public bool HistorySaved { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<Residence> Residences { get; set; } = [];
    public List<StatusEvent> Events { get; set; } = [];
    public Lease? Lease { get; set; }
}
public class Residence
{
    public int Id { get; set; }
    public int RentalApplicationId { get; set; }
    public RentalApplication RentalApplication { get; set; } = null!;
    [MaxLength(300)] public string Address { get; set; } = "";
    [MaxLength(120)] public string LandlordName { get; set; } = "";
    [MaxLength(40)] public string LandlordPhone { get; set; } = "";
    public DateOnly MoveIn { get; set; }
    public DateOnly MoveOut { get; set; }
}
public class StatusEvent
{
    public int Id { get; set; }
    public int RentalApplicationId { get; set; }
    public RentalApplication RentalApplication { get; set; } = null!;
    public ApplicationStatus? FromStatus { get; set; }
    public ApplicationStatus ToStatus { get; set; }
    public ReviewOutcome? Outcome { get; set; }
    public string ActorId { get; set; } = "";
    public AppUser Actor { get; set; } = null!;
    public DateTimeOffset At { get; set; }
    [MaxLength(2000)] public string? Comment { get; set; }
}
public class Lease
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public int RentalApplicationId { get; set; }
    public RentalApplication RentalApplication { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal MonthlyRent { get; set; }
}
