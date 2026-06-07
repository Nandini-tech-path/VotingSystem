using System.ComponentModel.DataAnnotations;

namespace VotingSystem.Web.ViewModels;

public class VoterLoginViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
