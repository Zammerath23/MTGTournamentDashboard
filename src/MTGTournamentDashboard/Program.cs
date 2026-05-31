using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MTGTournamentDashboard.Classifier;
using MTGTournamentDashboard.Components;
using MTGTournamentDashboard.Data;
using MTGTournamentDashboard.Querying;
using MTGTournamentDashboard.Sync;
using MTGTournamentDashboard.Sync.Melee;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // ContentRoot = carpeta del binario (sibling de appsettings*.json y wwwroot), estable frente
    // al CWD. El estado mutable (DB, cache, logs) va a OTRAS raíces, no aquí: ver AppPaths.
    var contentRoot = AppPaths.ResolveContentRoot();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = contentRoot
    });

    // Overrides del usuario, hermanos del .exe. El publish NUNCA los pisa (a diferencia de
    // appsettings.json, que son los defaults versionados). Mergea sobre la config base.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

    // Resolvemos las raíces de datos/logs una sola vez y las compartimos: connection string,
    // Serilog y el singleton AppPaths cuelgan todos de aquí.
    var (dataRoot, logRoot) = AppPaths.ResolveRoots(builder.Configuration);
    var appPaths = new AppPaths(dataRoot, logRoot);

    // Sinks definidos en código (no en appsettings) para poder anclar el File sink a una ruta
    // ABSOLUTA bajo LogRoot; appsettings solo aporta MinimumLevel/Enrich.
    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(logRoot, "app-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14));

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // La BD vive bajo DataRoot\db (ruta absoluta), independiente del CWD y de dónde esté el .exe.
    // Conservamos los demás parámetros de la connection string de appsettings (pragmas, cache…)
    // y solo forzamos el DataSource.
    var rawConn = builder.Configuration.GetConnectionString("MetaDb") ?? "Data Source=meta.db";
    var csb = new SqliteConnectionStringBuilder(rawConn) { DataSource = appPaths.DbPath };
    builder.Services.AddDbContextFactory<MetaDbContext>(opt => opt.UseSqlite(csb.ToString()));

    builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
    builder.Services.AddSingleton(appPaths);
    builder.Services.AddSingleton<SyncProgress>();
    builder.Services.AddSingleton<MtgoDecklistCacheClient>();
    builder.Services.AddSingleton<MtgoFormatDataClient>();
    builder.Services.AddSingleton<ArchetypeRulesLoader>();
    builder.Services.AddSingleton<SyncRunner>();
    builder.Services.AddScoped<TournamentJsonReader>();
    builder.Services.AddScoped<SyncService>();
    builder.Services.AddScoped<ClassifierService>();
    builder.Services.AddScoped<MetagameQuery>();

    builder.Services.AddHttpClient<MeleeApiClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
    builder.Services.AddScoped<MeleeDirectSyncService>();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MetaDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.Migrate();
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    // App local de un solo usuario servida en http://localhost:5000; sin endpoint https configurado,
    // UseHttpsRedirection solo emitía el warning "Failed to determine the https port for redirect".

    app.UseStaticFiles();
    app.UseAntiforgery();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Run();
}
catch (HostAbortedException)
{
    // expected when running EF Core design-time tools (dotnet ef ...)
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
