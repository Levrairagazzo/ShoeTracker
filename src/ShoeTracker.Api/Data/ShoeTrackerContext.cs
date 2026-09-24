using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Data;

public class ShoeTrackerContext(DbContextOptions<ShoeTrackerContext> options) : DbContext(options)
{
    public DbSet<Shoe> Shoes => Set<Shoe>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shoe>()
            .HasMany(s => s.Runs)
            .WithOne(r => r.Shoe)
            .HasForeignKey(r => r.ShoeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasMany<Shoe>()
            .WithOne(s => s.User)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<User>()
            .HasMany<Run>()
            .WithOne(r => r.User)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Run>()
            .Property(r => r.Source)
            .HasConversion<string>();

        modelBuilder.Entity<Run>()
            .HasIndex(r => new { r.UserId, r.StravaActivityId })
            .IsUnique();
    }
}
