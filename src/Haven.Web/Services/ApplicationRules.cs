using System.ComponentModel.DataAnnotations;
using Haven.Web.Domain;
using Haven.Web.ViewModels;
namespace Haven.Web.Services;

public sealed class RuleException(string message, string field = "") : Exception(message) { public string Field { get; } = field; }
public static class ApplicationRules
{
    public static bool Editable(ApplicationStatus status) => status is ApplicationStatus.Draft or ApplicationStatus.Returned;
    public static bool Withdrawable(ApplicationStatus status) => status is ApplicationStatus.Draft or ApplicationStatus.Returned or ApplicationStatus.Submitted;
    public static bool Covers(DateOnly start, DateOnly end, DateOnly day) => start <= day && day < end;
    public static bool Overlaps(DateOnly aStart, DateOnly aEnd, DateOnly bStart, DateOnly bEnd) => aStart < bEnd && bStart < aEnd;
    public static void RequireEditable(ApplicationStatus status)
    {
        if (!Editable(status)) throw new RuleException("This application is read-only in its current status.");
    }
    public static void RequireType(bool active, int? originalType, int selectedType)
    {
        if (!active && originalType != selectedType) throw new RuleException("Choose an active unit type.", "UnitTypeId");
    }
    public static void RequireReview(ApplicationStatus status, ReviewOutcome outcome, string? comment)
    {
        if (status != ApplicationStatus.Submitted) throw new RuleException("Only submitted applications can be reviewed.");
        if (!Enum.IsDefined(outcome)) throw new RuleException("Choose a valid outcome.", "Outcome");
        if (outcome != ReviewOutcome.Approve && string.IsNullOrWhiteSpace(comment)) throw new RuleException("A comment is required for Return and Deny.", "Comment");
    }
    public static IEnumerable<ValidationResult> ValidateInformation(InformationInput input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, true);
        return results;
    }
    public static IEnumerable<ValidationResult> ValidateResidence(ResidenceInput input, DateOnly today)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, true);
        if (input.MoveOut < input.MoveIn) results.Add(new("Move-out must be on or after move-in.", [nameof(input.MoveOut)]));
        if (input.MoveOut > today) results.Add(new("Prior residences must end on or before today.", [nameof(input.MoveOut)]));
        return results;
    }
}
