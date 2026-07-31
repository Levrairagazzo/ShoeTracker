using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Data;

public class ShoeTrackerContext(DbContextOptions<ShoeTrackerContext> options) : DbContext(options)
{
    public DbSet<Shoe> Shoes => Set<Shoe>();

    public DbSet<Run> Runs => Set<Run>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shoe>()
            .HasMany(s => s.Runs)
            .WithOne(r => r.Shoe)
            .HasForeignKey(r => r.ShoeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
