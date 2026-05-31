#requires -Version 5.1
<#
.SYNOPSIS
  Build a self-contained single-file exe of MTGTournamentDashboard for Windows x64
  and deploy ONLY the immutable artifacts (exe + appsettings.json + wwwroot) to the
  install directory.

.DESCRIPTION
  Separacion de concerns (ver AppPaths.cs):
    - Binarios + config  -> $InstallDir  (F:\Aplicaciones\MTGTournamentDashboard por defecto)
    - Estado mutable      -> F:\AppData\MTGTournamentDashboard   (DB + cache git)   [NO lo toca este script]
    - Logs                -> F:\Logs\MTGTournamentDashboard                          [NO lo toca este script]
  Las rutas de datos/logs se inyectan por appsettings.Local.json (no versionado), que este
  script copia al destino SOLO si aun no existe, para no pisar ediciones del usuario.
#>

param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$InstallDir = 'F:\Aplicaciones\MTGTournamentDashboard'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

$projectDir = Join-Path $repoRoot 'src\MTGTournamentDashboard'
$project = Join-Path $projectDir 'MTGTournamentDashboard.csproj'
$publishDir = Join-Path $repoRoot ('artifacts\publish\' + $Runtime)

Write-Host "==> Restoring..." -ForegroundColor Cyan
dotnet restore $project

Write-Host "==> Publishing single-file self-contained ($Runtime, $Configuration)..." -ForegroundColor Cyan
# Limpiamos la salida de build: ya NO contiene estado de runtime, asi que borrarla es seguro.
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $publishDir

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}

# Artefactos inmutables: se refrescan en cada publish (machacarlos es lo esperado).
Copy-Item -Force (Join-Path $publishDir 'MTGTournamentDashboard.exe') (Join-Path $InstallDir 'MTGTournamentDashboard.exe')
Copy-Item -Force (Join-Path $publishDir 'appsettings.json')           (Join-Path $InstallDir 'appsettings.json')

# .NET 9/10 Web SDK no embebe wwwroot dentro del self-extract bundle (el nuevo pipeline de
# MapStaticAssets espera servir desde FS). Lo copiamos al destino para que UseStaticFiles lo
# encuentre. Filtramos .br/.gz/.map: el UseStaticFiles clasico no los sirve y triplican el tamano.
$wwwrootSrc = Join-Path $publishDir 'wwwroot'
$wwwrootDst = Join-Path $InstallDir 'wwwroot'
if (Test-Path $wwwrootSrc) {
    if (Test-Path $wwwrootDst) { Remove-Item -Recurse -Force $wwwrootDst }
    Copy-Item -Recurse $wwwrootSrc $wwwrootDst
    Get-ChildItem $wwwrootDst -Recurse -Include '*.br', '*.gz', '*.map' -File | Remove-Item -Force
}

# Override de maquina: copiar SOLO si no existe en destino (preserva ediciones del usuario).
# El de la primera publicacion sale del repo (gitignored); en sucesivas el destino manda.
$localSrc = Join-Path $projectDir 'appsettings.Local.json'
$localDst = Join-Path $InstallDir 'appsettings.Local.json'
if ((Test-Path $localSrc) -and -not (Test-Path $localDst)) {
    Copy-Item $localSrc $localDst
    Write-Host "    appsettings.Local.json copiado (primera vez)." -ForegroundColor DarkGray
}
elseif (Test-Path $localDst) {
    Write-Host "    appsettings.Local.json ya existe en destino: se respeta." -ForegroundColor DarkGray
}

if (Test-Path (Join-Path $repoRoot 'README.md')) {
    Copy-Item -Force (Join-Path $repoRoot 'README.md') (Join-Path $InstallDir 'README.md')
}

# TODO: si activamos firma con cert auto-firmado, descomentar y rellenar thumbprint.
# $cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object { $_.Subject -like '*MTGTournamentDashboard*' } | Select-Object -First 1
# if ($cert) {
#     Set-AuthenticodeSignature -FilePath (Join-Path $InstallDir 'MTGTournamentDashboard.exe') -Certificate $cert -TimestampServer 'http://timestamp.digicert.com'
# }

Write-Host ""
Write-Host "==> Done. Installed to $InstallDir :" -ForegroundColor Green
Get-ChildItem $InstallDir | Format-Table Name, Length -AutoSize

Write-Host ""
Write-Host "Estado en (segun appsettings.Local.json):" -ForegroundColor Yellow
Write-Host "  DB/cache : F:\AppData\MTGTournamentDashboard"
Write-Host "  Logs     : F:\Logs\MTGTournamentDashboard"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Ejecuta $InstallDir\MTGTournamentDashboard.exe."
Write-Host "  2. Abre http://localhost:5000 en el navegador."
Write-Host "  3. Lanza la sincronizacion inicial (Sync)."
