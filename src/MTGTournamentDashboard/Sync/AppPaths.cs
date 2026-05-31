namespace MTGTournamentDashboard.Sync;

/// <summary>
/// Single source of truth for on-disk locations. Separates three concerns that must NOT
/// share a folder:
///   - <b>Binaries + config</b> (immutable, redistributable) → <see cref="ResolveContentRoot"/>:
///     carpeta del .exe en publish, carpeta de salida (bin) en <c>dotnet run</c>. Ahí viven
///     appsettings.json/appsettings.Local.json y wwwroot.
///   - <b>Estado mutable</b> (DB + cache git regenerable) → <see cref="DataRoot"/>.
///   - <b>Logs</b> → <see cref="LogRoot"/>.
/// Las rutas de datos/logs NO se anclan a la carpeta del .exe: así un mismo binario lanzado
/// desde cualquier sitio (dev, F:\Aplicaciones, Program Files) usa SIEMPRE la misma BD.
/// </summary>
public sealed class AppPaths
{
    public const string AppName = "MTGTournamentDashboard";

    /// <summary>Root for durable + regenerable state (contains <c>db\</c> and <c>cache\</c>).</summary>
    public string DataRoot { get; }
    /// <summary>Durable DB file. Backupeable; no incluye el cache regenerable.</summary>
    public string DbPath { get; }
    /// <summary>Regenerable git clones live here; safe to wipe.</summary>
    public string CacheDirectory { get; }
    public string MtgoDecklistCachePath { get; }
    public string MtgoFormatDataPath { get; }
    /// <summary>Rolling log directory.</summary>
    public string LogRoot { get; }

    public AppPaths(string dataRoot, string logRoot)
    {
        DataRoot = dataRoot;
        LogRoot = logRoot;

        var dbDir = Path.Combine(dataRoot, "db");
        DbPath = Path.Combine(dbDir, "meta.db");

        CacheDirectory = Path.Combine(dataRoot, "cache");
        MtgoDecklistCachePath = Path.Combine(CacheDirectory, "MTGODecklistCache");
        MtgoFormatDataPath = Path.Combine(CacheDirectory, "MTGOFormatData");

        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogRoot);
    }

    /// <summary>
    /// Folder where the binary + sibling config (appsettings*.json) + wwwroot live.
    /// Publish single-file: carpeta del .exe (vía <see cref="Environment.ProcessPath"/>; no usamos
    /// <see cref="AppContext.BaseDirectory"/> porque con IncludeAllContentForSelfExtract apunta a la
    /// extracción en %TEMP%). Dev (<c>dotnet run</c>): la carpeta bin de salida, donde el SDK copia
    /// appsettings.json y wwwroot. Estable frente al CWD desde el que se lance el proceso.
    /// </summary>
    public static string ResolveContentRoot()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var exeName = Path.GetFileNameWithoutExtension(exePath);
            if (exeName.Equals(AppName, StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
        }
        // dotnet run / dotnet ef: ProcessPath es dotnet.exe → la salida bin tiene appsettings + wwwroot.
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Resolves (DataRoot, LogRoot) from, in order of precedence:
    ///   1. Env var <c>MTGDASH_DATA_DIR</c> (escape para portable/CI/tests).
    ///   2. <c>Paths:DataRoot</c> / <c>Paths:LogRoot</c> en config (machine-specific; vive en
    ///      appsettings.Local.json, no versionado).
    ///   3. Default machine-agnostic: <c>%LOCALAPPDATA%\MTGTournamentDashboard</c> (+ <c>\logs</c>).
    /// Nunca se hardcodea una unidad concreta: la convención de la máquina se inyecta por config.
    /// </summary>
    public static (string DataRoot, string LogRoot) ResolveRoots(IConfiguration config)
    {
        var envData = Environment.GetEnvironmentVariable("MTGDASH_DATA_DIR");

        string dataRoot;
        if (!string.IsNullOrWhiteSpace(envData))
        {
            dataRoot = envData;
        }
        else if (config["Paths:DataRoot"] is { Length: > 0 } cfgData)
        {
            dataRoot = cfgData;
        }
        else
        {
            dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppName);
        }
        dataRoot = Path.GetFullPath(dataRoot);

        string logRoot = config["Paths:LogRoot"] is { Length: > 0 } cfgLog
            ? Path.GetFullPath(cfgLog)
            : Path.Combine(dataRoot, "logs");

        return (dataRoot, logRoot);
    }
}
