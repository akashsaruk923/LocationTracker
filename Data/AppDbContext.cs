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
            entity.Property(p => p.Source).HasMaxLength(8).IsRequired().HasDefaultValue("gps");
            entity.Property(p => p.ClientId).HasMaxLength(64).IsRequired();
            entity.Property(p => p.GoogleMapsUrl).HasMaxLength(128);
            entity.Property(p => p.City).HasMaxLength(128);
            entity.Property(p => p.Region).HasMaxLength(128);
            entity.Property(p => p.Country).HasMaxLength(128);
            entity.Property(p => p.UserAgent).HasMaxLength(512);
            entity.Property(p => p.IpAddress).HasMaxLength(64);
            entity.HasIndex(p => p.ClientId);
            entity.HasIndex(p => p.CreatedAtUtc);
        });
    }
}
