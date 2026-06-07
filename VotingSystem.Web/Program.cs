using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using VotingSystem.Web.Data;
using VotingSystem.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Database configuration: use LocalDB/SQL Express for development and Azure SQL in production (if configured).
var isDevelopment = builder.Environment.IsDevelopment();
var defaultConn = builder.Configuration.GetConnectionString("DefaultConnection");
var azureConn = builder.Configuration.GetConnectionString("AzureSqlConnection");
// If the default connection explicitly points to a local SQLite file, prefer it regardless of environment.
string connectionToUse;

if (builder.Environment.IsDevelopment())
{
    connectionToUse = defaultConn;
}
else
{
    connectionToUse = !string.IsNullOrEmpty(azureConn)
        ? azureConn
        : defaultConn;
}
builder.Services.AddDbContext<VotingDbContext>(options =>
{
    if (!string.IsNullOrEmpty(connectionToUse) && connectionToUse.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionToUse);
    }
    else
    {
        options.UseSqlServer(connectionToUse);
    }
});

// Authentication: always register cookie auth for voters; only add Azure AD when configured.
var azureSection = builder.Configuration.GetSection("AzureAd");
var azureClientId = azureSection["ClientId"];
var azureTenantId = azureSection["TenantId"];
var azureAdConfigured = !string.IsNullOrEmpty(azureClientId) && !azureClientId.Contains("<")
                      && !string.IsNullOrEmpty(azureTenantId) && !azureTenantId.Contains("<");

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = azureAdConfigured ? OpenIdConnectDefaults.AuthenticationScheme : CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Voter/Login";
    options.AccessDeniedPath = "/Home/AccessDenied";
    options.Cookie.Name = "VotingSystemAuth";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

if (azureAdConfigured)
{
    authBuilder.AddMicrosoftIdentityWebApp(azureSection, OpenIdConnectDefaults.AuthenticationScheme, CookieAuthenticationDefaults.AuthenticationScheme);
    builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Events ??= new OpenIdConnectEvents();
        options.Events.OnTokenValidated = context =>
        {
            var email = context.Principal?.FindFirstValue(ClaimTypes.Email)
                        ?? context.Principal?.FindFirstValue("preferred_username");

            var allowedAdmins = builder.Configuration.GetSection("AdminUsers").Get<string[]>() ?? Array.Empty<string>();
            if (email != null && (allowedAdmins.Length == 0 || allowedAdmins.Contains(email, StringComparer.OrdinalIgnoreCase)))
            {
                var identity = context.Principal?.Identity as ClaimsIdentity;
                identity?.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            }
            else
            {
                // Do not fail the request pipeline if the user is not an admin; simply skip role assignment.
            }

            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("VoterOnly", policy => policy.RequireRole("Voter"));
});

builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<VotingDbContext>();
        if (app.Environment.IsDevelopment())
        {
            logger.LogInformation("Attempting to apply SQLite database migrations...");
            db.Database.Migrate();
        }
        else
        {
            logger.LogInformation("Attempting to create SQL Server database schema directly...");
            db.Database.EnsureCreated();
        }
        logger.LogInformation("Database setup completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating the database. This is typically caused by incorrect SQL connection strings or IP firewall restrictions.");
        // We catch the error instead of crashing, so the app still starts and you can see the log.
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapHub<VoteHub>("/voteHub");

app.Run();
