namespace VotingSystem.Web.Models;

public class Election
{
    public int ElectionId { get; set; }
    public string ElectionName { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "Draft";

    public ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
    public ICollection<Vote> Votes { get; set; } = new List<Vote>();
}
