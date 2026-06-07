using VotingSystem.Web.Models;

namespace VotingSystem.Web.ViewModels;

public class VoterActiveElectionViewModel
{
    public Election? Election { get; set; }
    public IEnumerable<Candidate>? Candidates { get; set; }
    public bool AlreadyVoted { get; set; }
    public int VoterId { get; set; }
}
