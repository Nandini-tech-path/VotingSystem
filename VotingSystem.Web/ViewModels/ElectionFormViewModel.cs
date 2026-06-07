namespace VotingSystem.Web.ViewModels;

public class ElectionFormViewModel
{
    public int ElectionId { get; set; }
    public string ElectionName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime EndDate { get; set; } = DateTime.UtcNow.Date.AddDays(7);
    public string Status { get; set; } = "Draft";
}
