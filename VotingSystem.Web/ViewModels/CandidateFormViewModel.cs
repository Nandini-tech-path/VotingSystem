namespace VotingSystem.Web.ViewModels;

public class CandidateFormViewModel
{
    public int CandidateId { get; set; }
    public int ElectionId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string ElectionName { get; set; } = string.Empty;
}
