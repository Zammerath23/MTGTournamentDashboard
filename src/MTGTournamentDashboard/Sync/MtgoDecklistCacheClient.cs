using LibGit2Sharp;
using Microsoft.Extensions.Options;

namespace MTGTournamentDashboard.Sync;

public sealed class MtgoDecklistCacheClient
{
    private readonly AppPaths _paths;
    private readonly SyncOptions _options;
    private readonly ILogger<MtgoDecklistCacheClient> _logger;

    public MtgoDecklistCacheClient(AppPaths paths, IOptions<SyncOptions> options, ILogger<MtgoDecklistCacheClient> logger)
    {
        _paths = paths;
        _options = options.Value;
        _logger = logger;
    }

    public string LocalPath => _paths.MtgoDecklistCachePath;

    public void EnsureUpToDate(SyncProgress progress, CancellationToken ct)
    {
        var localPath = _paths.MtgoDecklistCachePath;
        var gitDir = Path.Combine(localPath, ".git");

        if (!Directory.Exists(gitDir))
        {
            progress.SetStep($"Clonando {_options.RepoUrl} (puede tardar varios minutos la primera vez)");
            if (Directory.Exists(localPath))
            {
                // empty, partial, or junk directory — wipe before clone
                Directory.Delete(localPath, recursive: true);
            }
            Directory.CreateDirectory(localPath);

            var cloneOptions = new CloneOptions
            {
                IsBare = false
            };
            cloneOptions.FetchOptions.OnTransferProgress = tp =>
            {
                if (tp.TotalObjects > 0 && tp.ReceivedObjects % 5000 == 0)
                {
                    progress.Info($"clone: {tp.ReceivedObjects}/{tp.TotalObjects} objects ({tp.ReceivedBytes / 1_000_000} MB)");
                }
                return !ct.IsCancellationRequested;
            };

            Repository.Clone(_options.RepoUrl, localPath, cloneOptions);
            progress.Info("Clone completado");
            return;
        }

        progress.SetStep("Actualizando MTGODecklistCache (fetch + reset)");
        using var repo = new Repository(localPath);
        var remote = repo.Network.Remotes["origin"];

        // If the configured RepoUrl differs from what the local clone uses (e.g. switching from
        // the dormant Badaro repo to Jiliac's active fork), point origin at the new URL so the
        // next fetch pulls the divergent history.
        if (!string.Equals(remote.Url, _options.RepoUrl, StringComparison.OrdinalIgnoreCase))
        {
            progress.Warn($"origin apuntaba a {remote.Url}; lo redirijo a {_options.RepoUrl}");
            repo.Network.Remotes.Update("origin", r => r.Url = _options.RepoUrl);
            remote = repo.Network.Remotes["origin"];
        }

        var fetchOptions = new FetchOptions();
        var refSpecs = remote.FetchRefSpecs.Select(r => r.Specification);
        Commands.Fetch(repo, remote.Name, refSpecs, fetchOptions, logMessage: null);

        var head = repo.Head.TrackedBranch ?? repo.Branches["origin/main"] ?? repo.Branches["origin/master"];
        if (head is null)
        {
            progress.Warn("No se encontró rama remota main/master; se usa HEAD local");
            return;
        }

        repo.Reset(ResetMode.Hard, head.Tip);
        progress.Info($"Actualizado a {head.Tip.Sha[..7]} ({head.Tip.MessageShort})");
    }
}
