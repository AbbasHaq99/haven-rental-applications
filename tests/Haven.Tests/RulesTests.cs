using Haven.Web.Domain;
using Haven.Web.Services;
using Haven.Web.ViewModels;
namespace Haven.Tests;

public class RulesTests
{
    [Theory]
    [InlineData(ApplicationStatus.Draft, true)]
    [InlineData(ApplicationStatus.Returned, true)]
    [InlineData(ApplicationStatus.Submitted, false)]
    [InlineData(ApplicationStatus.Approved, false)]
    [InlineData(ApplicationStatus.Denied, false)]
    [InlineData(ApplicationStatus.Withdrawn, false)]
    public void OnlyDraftAndReturnedAreEditable(ApplicationStatus status, bool expected) => Assert.Equal(expected, ApplicationRules.Editable(status));
    [Theory]
    [InlineData(ApplicationStatus.Draft, true)]
    [InlineData(ApplicationStatus.Returned, true)]
    [InlineData(ApplicationStatus.Submitted, true)]
    [InlineData(ApplicationStatus.Approved, false)]
    [InlineData(ApplicationStatus.Denied, false)]
    [InlineData(ApplicationStatus.Withdrawn, false)]
    public void TerminalApplicationsCannotBeWithdrawn(ApplicationStatus status, bool expected) => Assert.Equal(expected, ApplicationRules.Withdrawable(status));
    [Theory]
    [InlineData(ReviewOutcome.Return)]
    [InlineData(ReviewOutcome.Deny)]
    public void AdverseOutcomeRequiresNonWhitespaceComment(ReviewOutcome outcome) =>
        Assert.Throws<RuleException>(() => ApplicationRules.RequireReview(ApplicationStatus.Submitted, outcome, "  "));
    [Fact]
    public void ReviewOfTerminalStatusIsRejected() =>
        Assert.Throws<RuleException>(() => ApplicationRules.RequireReview(ApplicationStatus.Approved, ReviewOutcome.Approve, null));
    [Fact]
    public void InactiveTypeCanOnlyBeRetainedByItsExistingUnit()
    {
        ApplicationRules.RequireType(false, 3, 3);
        Assert.Throws<RuleException>(() => ApplicationRules.RequireType(false, null, 3));
        Assert.Throws<RuleException>(() => ApplicationRules.RequireType(false, 2, 3));
    }
    [Fact]
    public void LeaseUsesInclusiveStartAndExclusiveEnd()
    {
        var start = new DateOnly(2026, 10, 6); var end = start.AddMonths(12);
        Assert.True(ApplicationRules.Covers(start, end, start));
        Assert.True(ApplicationRules.Covers(start, end, end.AddDays(-1)));
        Assert.False(ApplicationRules.Covers(start, end, start.AddDays(-1)));
        Assert.False(ApplicationRules.Covers(start, end, end));
        Assert.False(ApplicationRules.Overlaps(start, end, end, end.AddMonths(12)));
    }
    [Fact]
    public void InformationErrorsPointToSpecificFields()
    {
        var errors = ApplicationRules.ValidateInformation(new() { Name = "Alex", Email = "invalid", Phone = "abc", CurrentAddress = "" }).ToList();
        Assert.Contains(errors, e => e.MemberNames.Contains("Email"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Phone"));
        Assert.Contains(errors, e => e.MemberNames.Contains("CurrentAddress"));
    }
    [Fact]
    public void ResidenceRequiresDatesInChronologicalOrderAndPast()
    {
        var today = new DateOnly(2026, 10, 6);
        var input = new ResidenceInput { Address = "1 Main St", LandlordName = "Sam", LandlordPhone = "312-555-0100", MoveIn = today, MoveOut = today.AddDays(-1) };
        Assert.Contains(ApplicationRules.ValidateResidence(input, today), e => e.MemberNames.Contains("MoveOut"));
        input.MoveOut = today.AddDays(1);
        Assert.Contains(ApplicationRules.ValidateResidence(input, today), e => e.MemberNames.Contains("MoveOut"));
        input.MoveIn = null;
        Assert.Contains(ApplicationRules.ValidateResidence(input, today), e => e.MemberNames.Contains("MoveIn"));
    }
}
