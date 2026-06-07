using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VotingSystem.Web.Data;
using VotingSystem.Web.Hubs;
using VotingSystem.Web.Models;
using VotingSystem.Web.ViewModels;

namespace VotingSystem.Web.Controllers;

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = "Voter")]
public class VoterController : Controller
{
    private readonly VotingDbContext _db;
    private readonly IHubContext<VoteHub> _voteHub;

    public VoterController(VotingDbContext db, IHubContext<VoteHub> voteHub)
    {
        _db = db;
        _voteHub = voteHub;
    }

    [AllowAnonymous]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Voter"))
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new VoterLoginViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(VoterLoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var voter = await _db.Voters.FirstOrDefaultAsync(v => v.Email == model.Email && v.Status == "Active");
        if (voter == null || !BCrypt.Net.BCrypt.Verify(model.Password, voter.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, voter.Id.ToString()),
            new Claim(ClaimTypes.Name, voter.Name),
            new Claim(ClaimTypes.Email, voter.Email),
            new Claim(ClaimTypes.Role, "Voter")
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

        return RedirectToAction(nameof(Index));
    }

    [AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Voter"))
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new VoterRegistrationViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(VoterRegistrationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (await _db.Voters.AnyAsync(v => v.Email == model.Email))
        {
            ModelState.AddModelError(nameof(model.Email), "A voter with this email already exists.");
            return View(model);
        }

        var voter = new Voter
        {
            Name = model.Name,
            Email = model.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
            Status = "Active"
        };

        _db.Voters.Add(voter);
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, voter.Id.ToString()),
            new Claim(ClaimTypes.Name, voter.Name),
            new Claim(ClaimTypes.Email, voter.Email),
            new Claim(ClaimTypes.Role, "Voter")
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [Authorize(Roles = "Voter")]
    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var election = await _db.Elections
            .Include(e => e.Candidates)
            .FirstOrDefaultAsync(e => e.Status == "Active" && e.StartDate <= now && e.EndDate >= now);

        if (election == null)
        {
            return RedirectToAction(nameof(Results));
        }

        var voterId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var alreadyVoted = await _db.Votes.AnyAsync(v => v.VoterId == voterId && v.ElectionId == election.ElectionId);

        var model = new VoterActiveElectionViewModel
        {
            Election = election,
            Candidates = election.Candidates.OrderBy(c => c.CandidateName),
            AlreadyVoted = alreadyVoted,
            VoterId = voterId
        };

        return View(model);
    }

    [Authorize(Roles = "Voter")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CastVote(int candidateId)
    {
        var voterId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var candidate = await _db.Candidates.Include(c => c.Election).FirstOrDefaultAsync(c => c.CandidateId == candidateId);
        if (candidate == null)
        {
            return NotFound();
        }

        var election = candidate.Election;
        if (election == null || election.Status != "Active" || election.StartDate > DateTime.UtcNow || election.EndDate < DateTime.UtcNow)
        {
            return RedirectToAction(nameof(Index));
        }

        var alreadyVoted = await _db.Votes.AnyAsync(v => v.VoterId == voterId && v.ElectionId == election.ElectionId);
        if (alreadyVoted)
        {
            return RedirectToAction(nameof(Index));
        }

        var vote = new Vote
        {
            CandidateId = candidateId,
            VoterId = voterId,
            ElectionId = election.ElectionId,
            VoteDate = DateTime.UtcNow
        };

        _db.Votes.Add(vote);
        await _db.SaveChangesAsync();

        var results = await GetVoteCountsAsync(election.ElectionId);
        await _voteHub.Clients.All.SendAsync("VoteCountsUpdated", election.ElectionId, results);

        return RedirectToAction(nameof(VoteConfirmation), new { candidateId });
    }

    [Authorize(Roles = "Voter")]
    public async Task<IActionResult> VoteConfirmation(int candidateId)
    {
        var vote = await _db.Votes
            .Include(v => v.Candidate)
            .Include(v => v.Election)
            .FirstOrDefaultAsync(v => v.CandidateId == candidateId && v.VoterId == int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));

        if (vote == null)
        {
            return RedirectToAction(nameof(Index));
        }

        var model = new VoteConfirmationViewModel
        {
            CandidateName = vote.Candidate?.CandidateName ?? string.Empty,
            ElectionName = vote.Election?.ElectionName ?? string.Empty,
            VoteDate = vote.VoteDate
        };

        return View(model);
    }

    [Authorize(Roles = "Voter")]
    public async Task<IActionResult> Results(int? electionId)
    {
        Election? election;
        if (electionId.HasValue)
        {
            election = await _db.Elections.Include(e => e.Candidates).FirstOrDefaultAsync(e => e.ElectionId == electionId);
        }
        else
        {
            election = await _db.Elections
                .OrderByDescending(e => e.EndDate)
                .FirstOrDefaultAsync(e => e.Status == "Closed" || e.EndDate < DateTime.UtcNow);
        }

        if (election == null)
        {
            return View(new ElectionResultsViewModel());
        }

        var voteCounts = await GetVoteCountsAsync(election.ElectionId);
        var winner = voteCounts.OrderByDescending(c => c.VoteCount).FirstOrDefault();

        var model = new ElectionResultsViewModel
        {
            Election = election,
            CandidateVoteCounts = voteCounts,
            Winner = winner
        };

        return View(model);
    }

    private async Task<IEnumerable<CandidateVoteCount>> GetVoteCountsAsync(int electionId)
    {
        return await _db.Candidates
            .Where(c => c.ElectionId == electionId)
            .Select(c => new CandidateVoteCount
            {
                CandidateId = c.CandidateId,
                CandidateName = c.CandidateName,
                VoteCount = c.Votes.Count()
            })
            .OrderByDescending(c => c.VoteCount)
            .ToListAsync();
    }
}
