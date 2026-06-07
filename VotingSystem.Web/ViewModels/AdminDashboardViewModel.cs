using VotingSystem.Web.Models;

namespace VotingSystem.Web.ViewModels;

public class AdminDashboardViewModel
{
    public int TotalVoters { get; set; }
    public int TotalCandidates { get; set; }
    public int TotalVotes { get; set; }
    public Election? ActiveElection { get; set; }
}
