namespace VotingSystem.Web.Models;

public class Voter
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Status { get; set; } = "Active";

    public ICollection<Vote> Votes { get; set; } = new List<Vote>();
}
