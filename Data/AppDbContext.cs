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
            entity.Property(p => p.Village).HasMaxLength(128);
            entity.Property(p => p.City).HasMaxLength(128);
            entity.Property(p => p.District).HasMaxLength(128);
            entity.Property(p => p.Region).HasMaxLength(128);
            entity.Property(p => p.Country).HasMaxLength(128);
            entity.Property(p => p.Postcode).HasMaxLength(16);
            entity.Property(p => p.Address).HasMaxLength(512);
            entity.Property(p => p.UserAgent).HasMaxLength(512);
            entity.Property(p => p.IpAddress).HasMaxLength(64);
            // IST wall-clock stored without a timezone so a plain SELECT shows local time.
            entity.Property(p => p.CreatedAtIst).HasColumnType("timestamp without time zone");
            entity.Property(p => p.DeviceTimestampIst).HasColumnType("timestamp without time zone");
            entity.HasIndex(p => p.ClientId);
            entity.HasIndex(p => p.CreatedAtIst);
        });
    }
}
