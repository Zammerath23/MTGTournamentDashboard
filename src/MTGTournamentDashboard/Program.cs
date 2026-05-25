using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MTGTournamentDashboard.Components;
using MTGTournamentDashboard.Data;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services));

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddDbContextFactory<MetaDbContext>(opt =>
        opt.UseSqlite(builder.Configuration.GetConnectionString("MetaDb")
                      ?? "Data Source=meta.db"));

    // TODO: register sync hosted service + classifier here

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
    app.UseHttpsRedirection();

    app.UseAntiforgery();

    app.MapStaticAssets();
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
