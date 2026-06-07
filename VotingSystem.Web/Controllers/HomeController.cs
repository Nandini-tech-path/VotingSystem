using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VotingSystem.Web.Data;
using VotingSystem.Web.Models;
using VotingSystem.Web.ViewModels;

namespace VotingSystem.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly VotingDbContext _db;

    public HomeController(ILogger<HomeController> logger, VotingDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var activeElection = await _db.Elections
            .Include(e => e.Candidates)
            .FirstOrDefaultAsync(e => e.Status == "Active" && e.StartDate <= now && e.EndDate >= now);

        var model = new HomeIndexViewModel
        {
            ActiveElection = activeElection
        };

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var exceptionHandlerPathFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;
        ViewBag.ErrorMessage = exception?.Message;
        ViewBag.StackTrace = exception?.StackTrace;
        
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
