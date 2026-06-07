using VotingSystem.Web.Models;

namespace VotingSystem.Web.ViewModels;

public class ElectionResultsViewModel
{
    public Election? Election { get; set; }
    public IEnumerable<CandidateVoteCount> CandidateVoteCounts { get; set; } = new List<CandidateVoteCount>();
    public CandidateVoteCount? Winner { get; set; }
}

public class CandidateVoteCount
{
    public int CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public int VoteCount { get; set; }
}
