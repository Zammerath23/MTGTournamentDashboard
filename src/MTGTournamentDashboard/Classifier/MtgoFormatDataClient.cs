using LibGit2Sharp;
using Microsoft.Extensions.Options;
using MTGTournamentDashboard.Sync;

namespace MTGTournamentDashboard.Classifier;

public sealed class MtgoFormatDataClient
{
    private readonly AppPaths _paths;
    private readonly SyncOptions _options;
    private readonly ILogger<MtgoFormatDataClient> _logger;

    public MtgoFormatDataClient(AppPaths paths, IOptions<SyncOptions> options, ILogger<MtgoFormatDataClient> logger)
    {
        _paths = paths;
        _options = options.Value;
        _logger = logger;
    }

    public string LocalPath => _paths.MtgoFormatDataPath;

    /// <summary>Returns the short commit SHA the rules repo is currently at.</summary>
    public string EnsureUpToDate(Action<string> log, CancellationToken ct)
    {
        var localPath = LocalPath;
        var gitDir = Path.Combine(localPath, ".git");

        if (!Directory.Exists(gitDir))
        {
            log($"Clonando {_options.FormatDataRepoUrl}");
            if (Directory.Exists(localPath)) Directory.Delete(localPath, recursive: true);
            Directory.CreateDirectory(localPath);
            Repository.Clone(_options.FormatDataRepoUrl, localPath);
        }
        else
        {
            log("Actualizando MTGOFormatData (fetch + reset)");
            using var repo = new Repository(localPath);
            var remote = repo.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(r => r.Specification);
            Commands.Fetch(repo, remote.Name, refSpecs, new FetchOptions(), logMessage: null);

            var head = repo.Head.TrackedBranch
                ?? repo.Branches["origin/main"]
                ?? repo.Branches["origin/master"];
            if (head is not null)
            {
                repo.Reset(ResetMode.Hard, head.Tip);
            }
        }

        using var rOut = new Repository(localPath);
        var sha = rOut.Head.Tip.Sha[..7];
        log($"MTGOFormatData @ {sha} ({rOut.Head.Tip.MessageShort})");
        return sha;
    }
}
