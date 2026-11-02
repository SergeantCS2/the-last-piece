<#
Turns the staged viewer folder (publish/TheLastPiece, made by package-viewer.ps1) into a Windows
installer with Velopack: TheLastPiece-win-Setup.exe, a portable zip, and the update packages that
installed copies download from GitHub Releases.

  pwsh tools/make-installer.ps1 -Version 0.3.0 [-Out dist/installer]

Signing is optional and switches on by itself when these are set (GitHub repository settings):
  Variables: SIGN_ENDPOINT (e.g. https://eus.codesigning.azure.net/), SIGN_ACCOUNT, SIGN_PROFILE
  Secrets:   AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET
That is Azure Artifact Signing (formerly Trusted Signing). Without them the build is unsigned
and Windows SmartScreen warns on first run.
#>
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$Out = "dist/installer",
    [string]$VpkVersion = "1.2.161"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$stage = Join-Path $root "publish/TheLastPiece"
if (-not (Test-Path (Join-Path $stage "TheLastPiece.exe"))) { throw "Run tools/package-viewer.ps1 first; $stage has no TheLastPiece.exe" }

# vpk is Velopack's packaging tool (MIT). Pinned so builds don't change under us.
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    dotnet tool install -g vpk --version $VpkVersion
    if ($LASTEXITCODE -ne 0) { throw "couldn't install vpk $VpkVersion" }
    $env:PATH += [IO.Path]::PathSeparator + (Join-Path $HOME ".dotnet/tools")
}
$env:DOTNET_ROLL_FORWARD = "Major"

$outDir = Join-Path $root $Out
New-Item -ItemType Directory -Force $outDir | Out-Null

$vpkArgs = @(
    "pack",
    "--packId", "TheLastPiece",
    "--packVersion", $Version,
    "--packDir", $stage,
    "--mainExe", "TheLastPiece.exe",
    "--packTitle", "The Last Piece",
    "--packAuthors", "The Last Piece",
    "--outputDir", $outDir
)

$signing = $env:SIGN_ENDPOINT -and $env:SIGN_ACCOUNT -and $env:SIGN_PROFILE -and $env:AZURE_CLIENT_ID
if ($signing) {
    $meta = Join-Path ([IO.Path]::GetTempPath()) "tlp-signing.json"
    @{ Endpoint = $env:SIGN_ENDPOINT; CodeSigningAccountName = $env:SIGN_ACCOUNT; CertificateProfileName = $env:SIGN_PROFILE } |
        ConvertTo-Json | Set-Content $meta
    # Sign our own files only. mpv and Syncplay ship exactly as their projects released them.
    $vpkArgs += @("--azureTrustedSignFile", $meta, "--signExclude", "^(mpv|syncplay)[\\/]")
    Write-Host "Signing with Azure Artifact Signing (account $($env:SIGN_ACCOUNT), profile $($env:SIGN_PROFILE))"
} else {
    Write-Host "::notice::Installer is unsigned: signing settings aren't configured, so SmartScreen will warn. See README, Signing."
}

& vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }
Get-ChildItem $outDir | ForEach-Object { Write-Host ("  {0,7:N1} MB  {1}" -f ($_.Length / 1MB), $_.Name) }
