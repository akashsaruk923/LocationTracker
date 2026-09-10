using LocationTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocationTracker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LocationPing> LocationPings => Set<LocationPing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocationPing>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.ClientId).HasMaxLength(64).IsRequired();
            entity.Property(p => p.UserAgent).HasMaxLength(512);
            entity.Property(p => p.IpAddress).HasMaxLength(64);
            entity.HasIndex(p => p.ClientId);
            entity.HasIndex(p => p.CreatedAtUtc);
        });
    }
}
