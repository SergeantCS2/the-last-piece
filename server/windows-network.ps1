<#
.SYNOPSIS
  Lets other PCs reach a The Last Piece host running in WSL, or undoes that.

.DESCRIPTION
  WSL sits behind Windows. For other PCs to reach the host app (port 8080) and the Syncplay
  server (port 8999), Windows has to pass those ports on to WSL and Windows Firewall has to let
  them in. Two ways:

    -Mode Mirrored   WSL shares Windows' own network (Windows 11 22H2 or later). Viewers' real
                     addresses reach the host, so they can swap pieces with each other. Writes
                     networkingMode=mirrored to %UserProfile%\.wslconfig (backed up first) and
                     allows the ports through the Windows and Hyper-V firewalls. Needs one
                     "wsl --shutdown" afterwards.
    -Mode PortProxy  Windows forwards the ports to WSL's current address (any Windows 10 or 11).
                     Works straight away, but WSL's address can change after a restart: run this
                     again then.

  -Undo removes everything this script added: its firewall rules, its port forwards, and its
  .wslconfig change (the backup is put back).

  Run it in Windows PowerShell as administrator. The installer in WSL runs it for you.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File windows-network.ps1 -Mode PortProxy -WslAddress 172.28.144.5
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File windows-network.ps1 -Undo
#>
[CmdletBinding()]
param(
  [ValidateSet('Mirrored', 'PortProxy')] [string] $Mode = 'PortProxy',
  [string] $WslAddress = '',
  [int] $HttpPort = 8080,
  [int] $SyncplayPort = 8999,
  [int] $FetchPort = 6890,
  [string] $UserProfile = $env:USERPROFILE,
  [string] $ResultFile = '',
  [switch] $Undo
)

$ErrorActionPreference = 'Stop'
$Group = 'The Last Piece'
$HyperVPrefix = 'TheLastPiece-'
$WslVmCreatorId = '{40E0AC32-46A5-438A-A0B2-2B479E8F2E90}'   # WSL's id for Hyper-V firewall rules (Microsoft docs)
$WslConfig = Join-Path $UserProfile '.wslconfig'
$Backup = "$WslConfig.thelastpiece-backup"
$NoFile = "$WslConfig.thelastpiece-none"

function Result([string] $text) {
  Write-Host $text
  if ($ResultFile) { Set-Content -Path $ResultFile -Value $text -Encoding ascii }
}

function Test-Admin {
  $id = [Security.Principal.WindowsIdentity]::GetCurrent()
  (New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Remove-Ours {
  Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
  if (Get-Command Get-NetFirewallHyperVRule -ErrorAction SilentlyContinue) {
    Get-NetFirewallHyperVRule -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "$HyperVPrefix*" } | Remove-NetFirewallHyperVRule
  }
  foreach ($p in $HttpPort, $SyncplayPort, $FetchPort) {
    netsh interface portproxy delete v4tov4 listenport=$p listenaddress=0.0.0.0 2>$null | Out-Null
  }
}

# Sets or removes one key in one [section] of an ini-style file, keeping everything else.
function Set-IniValue([string] $path, [string] $section, [string] $key, [string] $value) {
  $lines = @(); if (Test-Path $path) { $lines = @(Get-Content $path) }
  $out = New-Object System.Collections.Generic.List[string]
  $inSection = $false; $done = $false; $sawSection = $false
  foreach ($line in $lines) {
    if ($line -match '^\s*\[(.+)\]\s*$') {
      if ($inSection -and -not $done -and $value) { $out.Add("$key=$value"); $done = $true }
      $inSection = ($Matches[1].Trim() -ieq $section); if ($inSection) { $sawSection = $true }
      $out.Add($line); continue
    }
    if ($inSection -and $line -match "^\s*$([regex]::Escape($key))\s*=") {
      if ($value -and -not $done) { $out.Add("$key=$value"); $done = $true }
      continue
    }
    $out.Add($line)
  }
  if ($value -and -not $done) {
    if (-not $sawSection) { $out.Add("[$section]") }
    elseif (-not $inSection) { $out.Add("[$section]") }
    $out.Add("$key=$value")
  }
  Set-Content -Path $path -Value $out -Encoding ascii
}

try {
  if (-not (Test-Admin)) { Result 'failed: run this as administrator (right-click PowerShell, Run as administrator)'; exit 1 }

  if ($Undo) {
    Remove-Ours
    if (Test-Path $Backup) { Move-Item -Force $Backup $WslConfig; Write-Host "Put back your earlier .wslconfig." }
    elseif (Test-Path $NoFile) { Remove-Item -Force $NoFile; if (Test-Path $WslConfig) { Remove-Item -Force $WslConfig }; Write-Host "Removed the .wslconfig this script made." }
    Result 'ok: undone. If WSL used mirrored networking, run "wsl --shutdown" once to go back.'
    exit 0
  }

  Remove-Ours   # start clean, so running it twice never doubles anything

  foreach ($p in @(@{ Port = $HttpPort; Proto = 'TCP'; What = 'host app' }, @{ Port = $SyncplayPort; Proto = 'TCP'; What = 'Syncplay' },
                   @{ Port = $FetchPort; Proto = 'TCP'; What = 'server downloads' }, @{ Port = $FetchPort; Proto = 'UDP'; What = 'server downloads' })) {
    New-NetFirewallRule -DisplayName "The Last Piece $($p.What) ($($p.Proto) $($p.Port))" -Group $Group -Direction Inbound `
      -Action Allow -Protocol $p.Proto -LocalPort $p.Port -Profile Any | Out-Null
  }

  if ($Mode -eq 'Mirrored') {
    if (-not (Test-Path $Backup) -and -not (Test-Path $NoFile)) {
      if (Test-Path $WslConfig) { Copy-Item $WslConfig $Backup } else { New-Item -ItemType File $NoFile | Out-Null }
    }
    Set-IniValue $WslConfig 'wsl2' 'networkingMode' 'mirrored'
    # Lets this PC open the host at its own network address too (Microsoft docs: hostAddressLoopback).
    Set-IniValue $WslConfig 'experimental' 'hostAddressLoopback' 'true'
    $note = ''
    if (Get-Command New-NetFirewallHyperVRule -ErrorAction SilentlyContinue) {
      try {
        foreach ($p in @(@{ Port = $HttpPort; Proto = 'TCP' }, @{ Port = $SyncplayPort; Proto = 'TCP' }, @{ Port = $FetchPort; Proto = 'TCP' }, @{ Port = $FetchPort; Proto = 'UDP' })) {
          New-NetFirewallHyperVRule -Name "$HyperVPrefix$($p.Proto)-$($p.Port)" -DisplayName "The Last Piece ($($p.Proto) $($p.Port))" `
            -Direction Inbound -VMCreatorId $WslVmCreatorId -Protocol $p.Proto -LocalPorts $p.Port -Action Allow | Out-Null
        }
      }
      catch { $note = " (The WSL firewall rules couldn't be added: $($_.Exception.Message). If a second PC can't connect, see the setup guide.)" }
    }
    Result "ok: mirrored networking set. Run ""wsl --shutdown"", then open WSL again.$note"
  }
  else {
    if (-not $WslAddress) { Result 'failed: -WslAddress is needed for port forwarding (in WSL: hostname -I)'; exit 1 }
    foreach ($p in $HttpPort, $SyncplayPort, $FetchPort) {
      netsh interface portproxy add v4tov4 listenport=$p listenaddress=0.0.0.0 connectport=$p connectaddress=$WslAddress | Out-Null
    }
    Result "ok: ports $HttpPort, $SyncplayPort and $FetchPort forwarded to WSL at $WslAddress."
  }
  exit 0
}
catch {
  Result "failed: $($_.Exception.Message)"
  exit 1
}
