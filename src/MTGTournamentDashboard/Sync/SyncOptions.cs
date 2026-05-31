namespace MTGTournamentDashboard.Sync;

public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    public int InitialHistoryMonths { get; set; } = 6;
    public int IntervalHours { get; set; } = 6;
    public SourcesOptions Sources { get; set; } = new();
    public string[] Formats { get; set; } = new[] { "Modern" };

    // Badaro/MTGODecklistCache (the original) went dormant on 2025-06-10.
    // Jiliac's fork is the active community-maintained source from 2025-06 onwards.
    public string RepoUrl { get; set; } = "https://github.com/Jiliac/MTGODecklistCache.git";
    public string FormatDataRepoUrl { get; set; } = "https://github.com/Badaro/MTGOFormatData.git";

    public MeleeDirectOptions MeleeDirect { get; set; } = new();

    public sealed class SourcesOptions
    {
        public bool Mtgo { get; set; } = true;
        public bool Melee { get; set; } = true;
        public bool ManaTraders { get; set; }
        public bool MtgoLeague { get; set; }
    }

    public sealed class MeleeDirectOptions
    {
        public int DaysBack { get; set; } = 7;
    }
}
