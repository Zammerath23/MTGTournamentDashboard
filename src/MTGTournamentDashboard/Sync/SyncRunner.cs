using Microsoft.Extensions.Options;
using MTGTournamentDashboard.Classifier;
using MTGTournamentDashboard.Sync.Melee;

namespace MTGTournamentDashboard.Sync;

public sealed class SyncRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SyncProgress _progress;
    private readonly ILogger<SyncRunner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;

    public SyncRunner(IServiceProvider serviceProvider, SyncProgress progress, ILogger<SyncRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _progress = progress;
        _logger = logger;
    }

    public bool TryStartSync() => RunDetached("Sync", async (sp, ct) =>
    {
        var svc = sp.GetRequiredService<SyncService>();
        await svc.RunAsync(ct);
    });

    public bool TryStartReclassifyAll() => RunDetached("Reclassify", async (sp, ct) =>
    {
        _progress.BeginRun("Reclasificación completa");
        try
        {
            var svc = sp.GetRequiredService<ClassifierService>();
            await svc.RunAsync(reclassifyAll: true, ct);
            _progress.EndRun(success: true);
        }
        catch (OperationCanceledException) { _progress.EndRun(success: false, error: "Cancelado"); throw; }
        catch (Exception ex) { _progress.EndRun(success: false, error: ex.Message); throw; }
    });

    public bool TryStartMeleeDirect() => RunDetached("MeleeDirect", async (sp, ct) =>
    {
        var svc = sp.GetRequiredService<MeleeDirectSyncService>();
        var opts = sp.GetRequiredService<IOptions<SyncOptions>>().Value;
        await svc.RunAsync(opts.MeleeDirect.DaysBack, ct);
    });

    public void Cancel()
    {
        _cts?.Cancel();
    }

    private bool RunDetached(string opName, Func<IServiceProvider, CancellationToken, Task> body)
    {
        if (!_gate.Wait(0)) return false;
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                await body(scope.ServiceProvider, _cts.Token);
            }
            catch (OperationCanceledException) { _logger.LogInformation("{Op} cancelled", opName); }
            catch (Exception ex) { _logger.LogError(ex, "{Op} run threw", opName); }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _gate.Release();
            }
        });
        return true;
    }
}
