using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DotaDraft.Api.Data;

public class AppDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Hero> Heroes => Set<Hero>();
    public DbSet<HeroMatchup> HeroMatchups => Set<HeroMatchup>();
    public DbSet<SeedingState> SeedingStates => Set<SeedingState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Hero>()
            .Property(h => h.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<HeroMatchup>()
            .HasKey(m => new { m.HeroId, m.AgainstHeroId });

        modelBuilder.Entity<SeedingState>()
            .Property(s => s.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<HeroMatchup>()
            .HasOne(m => m.Hero)
            .WithMany(h => h.Matchups)
            .HasForeignKey(m => m.HeroId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<HeroMatchup>()
            .HasOne(m => m.AgainstHero)
            .WithMany()
            .HasForeignKey(m => m.AgainstHeroId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}