using System.ComponentModel.DataAnnotations;
using Haven.Web.Domain;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
namespace Haven.Web.ViewModels;

public class RegisterInput
{
    [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 10), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = "";
    [Required] public string Role { get; set; } = Roles.Applicant;
}
public class LoginInput
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
public class PropertyInput
{
    public int Id { get; set; }
    public Guid Version { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [Required, StringLength(300)] public string Address { get; set; } = "";
}
public class UnitInput
{
    public int Id { get; set; }
    public int PropertyId { get; set; }
    public Guid Version { get; set; }
    [Required, StringLength(30), Display(Name = "Unit number")] public string Number { get; set; } = "";
    [Range(0, 20)] public int Bedrooms { get; set; } = 1;
    [Range(typeof(decimal), "0.01", "9999999.99"), Display(Name = "Monthly rent")] public decimal MonthlyRent { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Unit type")] public int UnitTypeId { get; set; }
    [ValidateNever] public List<SelectListItem> Types { get; set; } = [];
}
public class DeleteInput
{
    public int Id { get; set; }
    public Guid Version { get; set; }
    public string Kind { get; set; } = "";
    [ValidateNever] public string Label { get; set; } = "";
}
public class InformationInput
{
    [Required, StringLength(120), Display(Name = "Full name")] public string Name { get; set; } = "";
    [Required, Phone, StringLength(40)] public string Phone { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(300), Display(Name = "Current address")] public string CurrentAddress { get; set; } = "";
}
public class ResidenceInput
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public Guid Version { get; set; }
    [Required, StringLength(300)] public string Address { get; set; } = "";
    [Required, StringLength(120), Display(Name = "Landlord name")] public string LandlordName { get; set; } = "";
    [Required, Phone, StringLength(40), Display(Name = "Landlord phone")] public string LandlordPhone { get; set; } = "";
    [Required, DataType(DataType.Date), Display(Name = "Move-in date")] public DateOnly? MoveIn { get; set; }
    [Required, DataType(DataType.Date), Display(Name = "Move-out date")] public DateOnly? MoveOut { get; set; }
}
public class ReviewInput
{
    public int ApplicationId { get; set; }
    public Guid Version { get; set; }
    [Required] public ReviewOutcome? Outcome { get; set; }
    [StringLength(2000)] public string? Comment { get; set; }
    [DataType(DataType.Date), Display(Name = "Lease start date")] public DateOnly? StartDate { get; set; }
}
public class ApplicationPage
{
    public int Id { get; set; }
    public Guid Version { get; set; }
    [Range(0, 2)] public int Section { get; set; }
    [ValidateNever] public InformationInput Information { get; set; } = new();
    [ValidateNever] public List<ResidenceInput> Residences { get; set; } = [];
    [ValidateNever] public bool Editable { get; set; }
    [ValidateNever] public bool InformationSaved { get; set; }
    [ValidateNever] public bool HistorySaved { get; set; }
    [ValidateNever] public ApplicationStatus Status { get; set; }
    [ValidateNever] public string UnitLabel { get; set; } = "";
    [ValidateNever] public decimal MonthlyRent { get; set; }
    [ValidateNever] public List<StatusEvent> Events { get; set; } = [];
    [ValidateNever] public List<FeedbackItem> Feedback { get; set; } = [];
    [ValidateNever] public Lease? Lease { get; set; }
}
public record UnitCard(int Id, int PropertyId, string PropertyName, string Address, string Number, int Bedrooms, decimal MonthlyRent, string Type, bool TypeActive, bool Available, Guid Version);
public record PropertyCard(int Id, string Name, string Address, Guid Version, List<UnitCard> Units);
public record CatalogPage(List<PropertyCard> Properties, bool Manager);
public record ApplicationRow(int Id, string Name, string Property, string Unit, ApplicationStatus Status, DateTimeOffset UpdatedAt);
public class ApplicationList
{
    public ApplicationStatus? Status { get; set; }
    public int? PropertyId { get; set; }
    public string Sort { get; set; } = "updated";
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 12d));
    public List<ApplicationRow> Rows { get; set; } = [];
    public List<SelectListItem> Properties { get; set; } = [];
}
public record ApplicationStats(int Drafts, int Submitted, int Returned, int Approved);

public record FeedbackItem(ReviewOutcome? Outcome, DateTimeOffset At, string? Comment);
