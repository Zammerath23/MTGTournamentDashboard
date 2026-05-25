using Microsoft.EntityFrameworkCore;
using MTGTournamentDashboard.Data.Entities;

namespace MTGTournamentDashboard.Data;

public class MetaDbContext : DbContext
{
    public MetaDbContext(DbContextOptions<MetaDbContext> options) : base(options) { }

    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Deck> Decks => Set<Deck>();
    public DbSet<Round> Rounds => Set<Round>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Tournament>(e =>
        {
            e.HasIndex(t => new { t.Source, t.SourceUrl }).IsUnique();
            e.HasIndex(t => new { t.Format, t.Date });
            e.Property(t => t.Source).HasMaxLength(64);
            e.Property(t => t.SourceUrl).HasMaxLength(512);
            e.Property(t => t.Name).HasMaxLength(256);
            e.Property(t => t.Format).HasMaxLength(32);
            e.Property(t => t.SourceHash).HasMaxLength(64);
        });

        mb.Entity<Player>(e =>
        {
            e.HasIndex(p => new { p.Name, p.Handle });
            e.Property(p => p.Name).HasMaxLength(128);
            e.Property(p => p.Handle).HasMaxLength(128);
        });

        mb.Entity<Deck>(e =>
        {
            e.HasIndex(d => new { d.TournamentId, d.PlayerId });
            e.HasIndex(d => d.Archetype);
            e.Property(d => d.Archetype).HasMaxLength(128);
            e.Property(d => d.ArchetypeRulesVersion).HasMaxLength(64);

            e.HasOne(d => d.Tournament)
                .WithMany(t => t.Decks)
                .HasForeignKey(d => d.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(d => d.Player)
                .WithMany(p => p.Decks)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Round>(e =>
        {
            e.HasIndex(r => new { r.TournamentId, r.RoundNumber });

            e.HasOne(r => r.Tournament)
                .WithMany(t => t.Rounds)
                .HasForeignKey(r => r.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.DeckA)
                .WithMany()
                .HasForeignKey(r => r.DeckAId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.DeckB)
                .WithMany()
                .HasForeignKey(r => r.DeckBId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.WinnerDeck)
                .WithMany()
                .HasForeignKey(r => r.WinnerDeckId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
