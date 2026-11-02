<#
Builds the Windows viewer zip: TheLastPiece.exe + portable mpv (with our mpv.conf)
+ portable Syncplay. Runs in GitHub Actions; also works on a Windows PC with the
.NET 10 SDK and 7-Zip installed.

  pwsh tools/package-viewer.ps1 -Out dist

Third-party downloads come from their official GitHub releases. Asset names are
matched by pattern and the script stops if nothing (or more than one thing) matches,
so a renamed release fails loudly instead of shipping the wrong file.
#>
param(
    [string]$Out = "dist",
    [string]$SyncplayTag = "v1.7.6",
    [string]$MpvRepo = "shinchiro/mpv-winbuild-cmake"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$work = Join-Path $root "publish"
$stage = Join-Path $work "TheLastPiece"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage, (Join-Path $work "dl") | Out-Null

$headers = @{ "User-Agent" = "the-last-piece-packager" }
if ($env:GH_TOKEN) { $headers["Authorization"] = "Bearer $($env:GH_TOKEN)" }

function Get-Asset([string]$repo, [string]$tag, [string]$pattern) {
    $url = if ($tag) { "https://api.github.com/repos/$repo/releases/tags/$tag" } else { "https://api.github.com/repos/$repo/releases/latest" }
    $release = Invoke-RestMethod -Headers $headers -Uri $url
    $found = @($release.assets | Where-Object { $_.name -match $pattern })
    if ($found.Count -ne 1) {
        $names = ($release.assets | ForEach-Object name) -join ", "
        throw "Expected exactly one asset matching '$pattern' in $repo $($release.tag_name), found $($found.Count). Assets: $names"
    }
    $file = Join-Path $work "dl" $found[0].name
    Write-Host "Downloading $($found[0].name) from $repo $($release.tag_name)"
    Invoke-WebRequest -Headers $headers -Uri $found[0].browser_download_url -OutFile $file
    return @{ File = $file; Tag = $release.tag_name; Name = $found[0].name }
}

function Copy-FolderOf([string]$extracted, [string]$exeName, [string]$dest) {
    $exe = Get-ChildItem -Recurse -File -Path $extracted -Filter $exeName | Select-Object -First 1
    if (-not $exe) { throw "$exeName not found after extracting into $extracted" }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item -Recurse -Force (Join-Path $exe.DirectoryName "*") $dest
}

# 1. Our app, self-contained so friends don't need .NET installed.
dotnet publish (Join-Path $root "src/Tlp.Viewer/Tlp.Viewer.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 2. Syncplay portable (Apache-2.0).
$sp = Get-Asset "Syncplay/syncplay" $SyncplayTag '(?i)portable.*\.zip$'
$spDir = Join-Path $work "syncplay-x"
Expand-Archive -Path $sp.File -DestinationPath $spDir
Copy-FolderOf $spDir "Syncplay.exe" (Join-Path $stage "syncplay")

# 3. mpv (x86_64 baseline build, not -v3, so older CPUs work too).
$mpv = Get-Asset $MpvRepo "" '^mpv-x86_64-\d{8}-git-[0-9a-f]+\.7z$'
$mpvDir = Join-Path $work "mpv-x"
& 7z x $mpv.File "-o$mpvDir" -y | Out-Null
if ($LASTEXITCODE -ne 0) { throw "7z failed on $($mpv.Name)" }
Copy-FolderOf $mpvDir "mpv.exe" (Join-Path $stage "mpv")
New-Item -ItemType Directory -Force (Join-Path $stage "mpv/portable_config") | Out-Null
Copy-Item (Join-Path $root "viewer/mpv.conf") (Join-Path $stage "mpv/portable_config/mpv.conf")

# 4. Notices.
@"
The Last Piece viewer $((Get-Item (Join-Path $stage 'TheLastPiece.exe')).VersionInfo.ProductVersion)

Bundled third-party software, unmodified:
- Syncplay $($sp.Tag) ($($sp.Name)), Apache License 2.0, https://github.com/Syncplay/syncplay
- mpv ($($mpv.Name)) from $MpvRepo $($mpv.Tag), see mpv's own license files in the mpv folder, https://mpv.io
"@ | Set-Content (Join-Path $stage "THIRD-PARTY-NOTICES.txt")

New-Item -ItemType Directory -Force (Join-Path $root $Out) | Out-Null
$zip = Join-Path $root $Out "TheLastPiece-win-x64.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "Wrote $zip"
