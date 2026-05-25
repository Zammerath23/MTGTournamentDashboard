# MTG Tournament Dashboard

Herramienta personal para trackear torneos de Magic: The Gathering (Modern de momento), calcular winrate general por arquetipo y matrices de matchup.

## Stack

- .NET 10
- Blazor Server (UI)
- EF Core + SQLite (persistencia)
- Serilog (logging)
- LibGit2Sharp (sync con MTGODecklistCache)

## Fuentes de datos

- [MTGODecklistCache](https://github.com/Badaro/MTGODecklistCache): JSON normalizado de torneos MTGO, Melee.gg y ManaTraders.
- [MTGOFormatData](https://github.com/Badaro/MTGOFormatData): reglas de clasificación de arquetipos por formato.

## Estructura

```
MTGTournamentDashboard/
├── MTGTournamentDashboard.sln
├── src/
│   └── MTGTournamentDashboard/
│       ├── Components/   (Blazor UI)
│       ├── Data/         (EF Core: DbContext + Entities)
│       ├── Migrations/   (EF Core migrations)
│       └── Program.cs
├── tools/
│   └── publish.ps1       (single-file exe)
└── dotnet-tools.json     (dotnet-ef local)
```

## Cómo correrlo (dev)

```powershell
dotnet run --project src/MTGTournamentDashboard
```

Abre `https://localhost:xxxx` que indique la consola. La BBDD `meta.db` se crea en la carpeta de trabajo al primer arranque.

## Migraciones

```powershell
dotnet ef migrations add NombreDeMigracion --project src/MTGTournamentDashboard
dotnet ef database update --project src/MTGTournamentDashboard
```

> El programa aplica las migraciones pendientes al arrancar; el comando `database update` solo es necesario en escenarios excepcionales.

## Publicación single-file exe

```powershell
.\tools\publish.ps1
```

Genera `dist/MTGTournamentDashboard.exe` autocontenido para `win-x64`.

## Estado

Scaffold inicial. Pendiente:
- Servicio de sincronización con MTGODecklistCache.
- Clasificador de arquetipos vía MTGOFormatData.
- Páginas: Dashboard, Tournaments (con tabla), Metagame (gráfico + winrate general), Matchups (heatmap), Sync (control + log en vivo).
