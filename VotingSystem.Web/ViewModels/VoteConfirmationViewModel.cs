namespace VotingSystem.Web.ViewModels;

public class VoteConfirmationViewModel
{
    public string CandidateName { get; set; } = string.Empty;
    public string ElectionName { get; set; } = string.Empty;
    public DateTime VoteDate { get; set; }
}
