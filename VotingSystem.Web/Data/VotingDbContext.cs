using Microsoft.EntityFrameworkCore;
using VotingSystem.Web.Models;

namespace VotingSystem.Web.Data;

public class VotingDbContext : DbContext
{
    public VotingDbContext(DbContextOptions<VotingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Voter> Voters { get; set; } = null!;
    public DbSet<Election> Elections { get; set; } = null!;
    public DbSet<Candidate> Candidates { get; set; } = null!;
    public DbSet<Vote> Votes { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Voter>(entity =>
        {
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Name).IsRequired().HasMaxLength(120);
            entity.Property(v => v.Email).IsRequired().HasMaxLength(200);
            entity.HasIndex(v => v.Email).IsUnique();
            entity.Property(v => v.PasswordHash).IsRequired();
            entity.Property(v => v.Status).IsRequired().HasMaxLength(50);
        });

        builder.Entity<Election>(entity =>
        {
            entity.HasKey(e => e.ElectionId);
            entity.Property(e => e.ElectionName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.StartDate).IsRequired();
            entity.Property(e => e.EndDate).IsRequired();
        });

        builder.Entity<Candidate>(entity =>
        {
            entity.HasKey(c => c.CandidateId);
            entity.Property(c => c.CandidateName).IsRequired().HasMaxLength(160);
            entity.HasOne(c => c.Election)
                  .WithMany(e => e.Candidates)
                  .HasForeignKey(c => c.ElectionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Vote>(entity =>
        {
            entity.HasKey(v => v.VoteId);
            entity.Property(v => v.VoteDate).IsRequired();
            entity.HasOne(v => v.Candidate)
                  .WithMany(c => c.Votes)
                  .HasForeignKey(v => v.CandidateId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(v => v.Voter)
                  .WithMany(vr => vr.Votes)
                  .HasForeignKey(v => v.VoterId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(v => v.Election)
                  .WithMany(e => e.Votes)
                  .HasForeignKey(v => v.ElectionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(v => new { v.VoterId, v.ElectionId }).IsUnique();
        });

        var passwordHash = BCrypt.Net.BCrypt.HashPassword("Password123!");
        builder.Entity<Voter>().HasData(
            new Voter { Id = 1, Name = "Sample Voter", Email = "voter@example.com", PasswordHash = passwordHash, Status = "Active" }
        );

        builder.Entity<Election>().HasData(
            new Election
            {
                ElectionId = 1,
                ElectionName = "General Election",
                StartDate = DateTime.UtcNow.Date.AddDays(-2),
                EndDate = DateTime.UtcNow.Date.AddDays(5),
                Status = "Active"
            }
        );

        builder.Entity<Candidate>().HasData(
            new Candidate { CandidateId = 1, ElectionId = 1, CandidateName = "Alice Johnson" },
            new Candidate { CandidateId = 2, ElectionId = 1, CandidateName = "Robert Smith" }
        );
    }
}
