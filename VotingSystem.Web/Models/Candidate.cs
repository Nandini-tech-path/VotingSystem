namespace VotingSystem.Web.Models;

public class Candidate
{
    public int CandidateId { get; set; }
    public int ElectionId { get; set; }
    public string CandidateName { get; set; } = null!;

    public Election? Election { get; set; }
    public ICollection<Vote> Votes { get; set; } = new List<Vote>();
}
