using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VotingSystem.Web.Data;
using VotingSystem.Web.Hubs;
using VotingSystem.Web.Models;
using VotingSystem.Web.ViewModels;

namespace VotingSystem.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly VotingDbContext _db;
    private readonly IHubContext<VoteHub> _voteHub;
    private readonly IConfiguration _configuration;
    private readonly bool _azureAdConfigured;

    public AdminController(VotingDbContext db, IHubContext<VoteHub> voteHub, IConfiguration configuration)
    {
        _db = db;
        _voteHub = voteHub;
        _configuration = configuration;

        var azureSection = configuration.GetSection("AzureAd");
        var azureClientId = azureSection["ClientId"];
        var azureTenantId = azureSection["TenantId"];
        _azureAdConfigured = !string.IsNullOrEmpty(azureClientId) && !azureClientId.Contains("<")
                              && !string.IsNullOrEmpty(azureTenantId) && !azureTenantId.Contains("<");
    }

    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Dashboard));
        }

        if (_azureAdConfigured)
        {
            var authenticationProperties = new AuthenticationProperties
            {
                RedirectUri = returnUrl ?? Url.Action(nameof(Dashboard), "Admin")
            };

            return Challenge(authenticationProperties, OpenIdConnectDefaults.AuthenticationScheme);
        }

        var model = new AdminLoginViewModel
        {
            ReturnUrl = returnUrl ?? Url.Action(nameof(Dashboard), "Admin")
        };

        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AdminLoginViewModel model)
    {
        if (_azureAdConfigured)
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var localAdminEmail = _configuration["LocalAdmin:Email"] ?? "admin@local.test";
        var localAdminPassword = _configuration["LocalAdmin:Password"] ?? "Admin123!";

        if (!string.Equals(model.Email, localAdminEmail, StringComparison.OrdinalIgnoreCase)
            || model.Password != localAdminPassword)
        {
            ModelState.AddModelError(string.Empty, "Invalid admin credentials.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, model.Email),
            new Claim(ClaimTypes.Name, model.Email),
            new Claim(ClaimTypes.Email, model.Email),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

        return LocalRedirect(model.ReturnUrl ?? Url.Action(nameof(Dashboard), "Admin")!);
    }

    [AllowAnonymous]
    public IActionResult Logout()
    {
        if (_azureAdConfigured)
        {
            return SignOut(new AuthenticationProperties { RedirectUri = Url.Action("Index", "Home") },
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme);
        }

        return SignOut(new AuthenticationProperties { RedirectUri = Url.Action("Index", "Home") },
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<IActionResult> Dashboard()
    {
        var now = DateTime.UtcNow;
        var activeElection = await _db.Elections
            .Include(e => e.Candidates)
                .ThenInclude(c => c.Votes)
            .FirstOrDefaultAsync(e => e.Status == "Active" && e.StartDate <= now && e.EndDate >= now);

        var model = new AdminDashboardViewModel
        {
            TotalVoters = await _db.Voters.CountAsync(v => v.Status == "Active"),
            TotalCandidates = await _db.Candidates.CountAsync(),
            TotalVotes = await _db.Votes.CountAsync(),
            ActiveElection = activeElection
        };

        return View(model);
    }

    public async Task<IActionResult> Elections()
    {
        var elections = await _db.Elections.OrderByDescending(e => e.StartDate).ToListAsync();
        return View(elections);
    }

    public IActionResult CreateElection()
    {
        return View(new ElectionFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateElection(ElectionFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var election = new Election
        {
            ElectionName = model.ElectionName,
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Status = model.Status
        };

        _db.Elections.Add(election);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Elections));
    }

    public async Task<IActionResult> EditElection(int id)
    {
        var election = await _db.Elections.FindAsync(id);
        if (election == null)
        {
            return NotFound();
        }

        return View(new ElectionFormViewModel
        {
            ElectionId = election.ElectionId,
            ElectionName = election.ElectionName,
            StartDate = election.StartDate,
            EndDate = election.EndDate,
            Status = election.Status
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditElection(ElectionFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var election = await _db.Elections.FindAsync(model.ElectionId);
        if (election == null)
        {
            return NotFound();
        }

        election.ElectionName = model.ElectionName;
        election.StartDate = model.StartDate;
        election.EndDate = model.EndDate;
        election.Status = model.Status;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Elections));
    }

    public async Task<IActionResult> ActivateElection(int id)
    {
        var election = await _db.Elections.FindAsync(id);
        if (election == null)
        {
            return NotFound();
        }

        var activeElections = await _db.Elections.Where(e => e.Status == "Active").ToListAsync();
        foreach (var activeElection in activeElections)
        {
            activeElection.Status = "Closed";
        }

        election.Status = "Active";
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Elections));
    }

    public async Task<IActionResult> CloseElection(int id)
    {
        var election = await _db.Elections.FindAsync(id);
        if (election == null)
        {
            return NotFound();
        }

        election.Status = "Closed";
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Elections));
    }

    public async Task<IActionResult> Candidates(int? electionId)
    {
        if (!electionId.HasValue)
        {
            var now = DateTime.UtcNow;
            var activeElection = await _db.Elections
                .OrderByDescending(e => e.StartDate)
                .FirstOrDefaultAsync(e => e.Status == "Active" && e.StartDate <= now && e.EndDate >= now);

            if (activeElection != null)
            {
                return RedirectToAction(nameof(Candidates), new { electionId = activeElection.ElectionId });
            }

            var latestElection = await _db.Elections
                .OrderByDescending(e => e.StartDate)
                .FirstOrDefaultAsync();

            if (latestElection == null)
            {
                return RedirectToAction(nameof(Elections));
            }

            return RedirectToAction(nameof(Candidates), new { electionId = latestElection.ElectionId });
        }

        var election = await _db.Elections.Include(e => e.Candidates).FirstOrDefaultAsync(e => e.ElectionId == electionId.Value);
        if (election == null)
        {
            return NotFound();
        }

        ViewData["ElectionName"] = election.ElectionName;
        ViewData["ElectionId"] = election.ElectionId;
        return View(election.Candidates.OrderBy(c => c.CandidateName));
    }

    public async Task<IActionResult> CreateCandidate(int electionId)
    {
        var election = await _db.Elections.FindAsync(electionId);
        if (election == null)
        {
            return NotFound();
        }

        return View(new CandidateFormViewModel { ElectionId = electionId, ElectionName = election.ElectionName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCandidate(CandidateFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var candidate = new Candidate
        {
            ElectionId = model.ElectionId,
            CandidateName = model.CandidateName
        };

        _db.Candidates.Add(candidate);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Candidates), new { electionId = model.ElectionId });
    }

    public async Task<IActionResult> EditCandidate(int id)
    {
        var candidate = await _db.Candidates.Include(c => c.Election).FirstOrDefaultAsync(c => c.CandidateId == id);
        if (candidate == null)
        {
            return NotFound();
        }

        return View(new CandidateFormViewModel
        {
            CandidateId = candidate.CandidateId,
            ElectionId = candidate.ElectionId,
            CandidateName = candidate.CandidateName,
            ElectionName = candidate.Election?.ElectionName ?? string.Empty
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCandidate(CandidateFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var candidate = await _db.Candidates.FindAsync(model.CandidateId);
        if (candidate == null)
        {
            return NotFound();
        }

        candidate.CandidateName = model.CandidateName;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Candidates), new { electionId = candidate.ElectionId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCandidate(int id)
    {
        var candidate = await _db.Candidates
            .Include(c => c.Votes)
            .FirstOrDefaultAsync(c => c.CandidateId == id);
        if (candidate == null)
        {
            return NotFound();
        }

        var electionId = candidate.ElectionId;

        if (candidate.Votes.Any())
        {
            _db.Votes.RemoveRange(candidate.Votes);
        }

        _db.Candidates.Remove(candidate);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Candidates), new { electionId });
    }

    public async Task<IActionResult> ElectionResults(int id)
    {
        var election = await _db.Elections
            .Include(e => e.Candidates)
            .FirstOrDefaultAsync(e => e.ElectionId == id);
        if (election == null)
        {
            return NotFound();
        }

        var voteCounts = await GetVoteCountsAsync(id);
        var winner = voteCounts.OrderByDescending(c => c.VoteCount).FirstOrDefault();

        var model = new ElectionResultsViewModel
        {
            Election = election,
            CandidateVoteCounts = voteCounts,
            Winner = winner
        };

        return View(model);
    }


    public async Task<IActionResult> Voters()
    {
        var voters = await _db.Voters.OrderBy(v => v.Name).ToListAsync();
        return View(voters);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVoterStatus(int id)
    {
        var voter = await _db.Voters.FindAsync(id);
        if (voter == null)
        {
            return NotFound();
        }

        voter.Status = voter.Status == "Active" ? "Inactive" : "Active";
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Voters));
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
