#requires -Version 5.1
<#
.SYNOPSIS
  Build a self-contained single-file exe of MTGTournamentDashboard for Windows x64
  and stage it in dist\.
#>

param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

$project = 'src\MTGTournamentDashboard\MTGTournamentDashboard.csproj'
$publishDir = Join-Path $repoRoot ('artifacts\publish\' + $Runtime)
$distDir = Join-Path $repoRoot 'dist'

Write-Host "==> Restoring..." -ForegroundColor Cyan
dotnet restore $project

Write-Host "==> Publishing single-file self-contained ($Runtime, $Configuration)..." -ForegroundColor Cyan
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $publishDir

if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
New-Item -ItemType Directory -Path $distDir | Out-Null

Copy-Item (Join-Path $publishDir 'MTGTournamentDashboard.exe') (Join-Path $distDir 'MTGTournamentDashboard.exe')
Copy-Item (Join-Path $publishDir 'appsettings.json')           (Join-Path $distDir 'appsettings.json')

if (Test-Path (Join-Path $repoRoot 'README.md')) {
    Copy-Item (Join-Path $repoRoot 'README.md') (Join-Path $distDir 'README.md')
}

# TODO: si activamos firma con cert auto-firmado, descomentar y rellenar thumbprint.
# $cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object { $_.Subject -like '*MTGTournamentDashboard*' } | Select-Object -First 1
# if ($cert) {
#     Set-AuthenticodeSignature -FilePath (Join-Path $distDir 'MTGTournamentDashboard.exe') -Certificate $cert -TimestampServer 'http://timestamp.digicert.com'
# }

Write-Host ""
Write-Host "==> Done. Distributable contents:" -ForegroundColor Green
Get-ChildItem $distDir | Format-Table Name, Length -AutoSize

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Double-click MTGTournamentDashboard.exe."
Write-Host "  2. Abre http://localhost:5000 en el navegador."
Write-Host "  3. Lanza la sincronizacion inicial (Sync -> ultimos 6 meses)."
