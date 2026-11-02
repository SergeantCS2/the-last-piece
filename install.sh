#!/usr/bin/env bash
# The Last Piece: host installer for Linux and WSL (Windows Subsystem for Linux).
#
# Checks for and offers to install what the host needs (git, curl, Docker with Compose), fetches
# the app, writes server/.env with fresh passwords, starts the host app and the Syncplay server,
# and in WSL offers to set up Windows so other PCs can reach it. Every step asks first.
# Run it again any time to update, or to change the address or the files folder.
#
#   bash install.sh                 # from a copy of the repo
#   curl -fsSL https://raw.githubusercontent.com/SergeantCS2/the-last-piece/main/install.sh | bash
#
# Options:
#   --yes                 accept every default without asking (for scripts and CI)
#   --dir DIR             where to put the app (default: this repo, or ~/the-last-piece)
#   --public-url URL      the address other PCs use, like http://192.168.1.20:8080
#   --shelf DIR           the folder with the episodes to share
#   --group NAME          your group's name, shown as the title in everyone's app
#   --windows-network M   WSL only: mirrored, portproxy or skip (default: ask)
#   --no-start            write the settings but don't start anything
#   --dry-run             say what would happen, change nothing
#   -h, --help            this help
#
# Uninstall with uninstall.sh in the same folder.

set -euo pipefail

REPO_URL="https://github.com/SergeantCS2/the-last-piece.git"
ASSUME_YES=0 DRY_RUN=0 NO_START=0
INSTALL_DIR="" PUBLIC_URL="" SHELF="" GROUP="" WIN_NET=""

if [ -t 1 ]; then B=$'\033[1m' G=$'\033[32m' Y=$'\033[33m' R=$'\033[31m' N=$'\033[0m'; else B="" G="" Y="" R="" N=""; fi
say()  { printf '%s\n' "$*"; }
step() { printf '\n%s== %s%s\n' "$B" "$*" "$N"; }
ok()   { printf '  %sok%s  %s\n' "$G" "$N" "$*"; }
warn() { printf '  %s!!%s  %s\n' "$Y" "$N" "$*"; }
die()  { printf '\n%sStopped:%s %s\n' "$R" "$N" "$*" >&2; exit 1; }
run()  { if [ "$DRY_RUN" = 1 ]; then say "  would run: $*"; else "$@"; fi; }

usage() { sed -n '2,25p' "$0" 2>/dev/null | sed 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --yes|-y) ASSUME_YES=1 ;;
    --dir) INSTALL_DIR="${2:?--dir needs a folder}"; shift ;;
    --public-url) PUBLIC_URL="${2:?--public-url needs an address}"; shift ;;
    --shelf) SHELF="${2:?--shelf needs a folder}"; shift ;;
    --group) GROUP="${2?--group needs a name}"; shift ;;
    --windows-network) WIN_NET="${2:?--windows-network needs mirrored, portproxy or skip}"; shift ;;
    --no-start) NO_START=1 ;;
    --dry-run) DRY_RUN=1 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option $1 (see --help)" ;;
  esac
  shift
done

# Questions come from the keyboard even when the script itself arrives through a pipe.
TTY=/dev/tty
if [ "$ASSUME_YES" = 0 ] && ! { [ -r "$TTY" ] && : < "$TTY"; } 2>/dev/null; then
  die "no keyboard to ask questions on. Run it in a terminal, or add --yes to take the defaults."
fi

# ask "Question" "default" -> prints the answer
ask() {
  local q="$1" def="${2-}" ans
  if [ "$ASSUME_YES" = 1 ]; then printf '%s' "$def"; return; fi
  if [ -n "$def" ]; then printf '  %s [%s]: ' "$q" "$def" > "$TTY"; else printf '  %s: ' "$q" > "$TTY"; fi
  IFS= read -r ans < "$TTY" || true
  printf '%s' "${ans:-$def}"
}

# confirm "Question" y|n -> exit status
confirm() {
  local q="$1" def="${2:-y}" ans hint="[Y/n]"
  [ "$def" = n ] && hint="[y/N]"
  if [ "$ASSUME_YES" = 1 ]; then [ "$def" = y ]; return; fi
  printf '  %s %s ' "$q" "$hint" > "$TTY"
  IFS= read -r ans < "$TTY" || true
  ans="${ans:-$def}"
  case "$ans" in [Yy]*) return 0 ;; *) return 1 ;; esac
}

# --- where are we -----------------------------------------------------------------------------

step "Checking this machine"
[ "$(uname -s)" = Linux ] || die "this installer is for Linux and WSL. On Windows, open your WSL terminal (for example Ubuntu) and run it there."

IN_WSL=0
grep -qi microsoft /proc/version 2>/dev/null && IN_WSL=1
# shellcheck source=/dev/null
. /etc/os-release 2>/dev/null || true
ok "${PRETTY_NAME:-Linux}$([ "$IN_WSL" = 1 ] && printf ', in WSL') ($(uname -m))"

SUDO=""
if [ "$(id -u)" != 0 ]; then
  command -v sudo >/dev/null || die "some steps need root, and sudo isn't installed. Run this as root, or install sudo."
  SUDO="sudo"
fi

PKG=""
for m in apt-get dnf yum zypper pacman apk; do command -v "$m" >/dev/null && { PKG="$m"; break; }; done

pkg_install() {
  case "$PKG" in
    apt-get) run $SUDO apt-get update -qq && run $SUDO env DEBIAN_FRONTEND=noninteractive apt-get install -y -qq "$@" ;;
    dnf|yum) run $SUDO "$PKG" install -y "$@" ;;
    zypper)  run $SUDO zypper --non-interactive install "$@" ;;
    pacman)  run $SUDO pacman -Sy --noconfirm --needed "$@" ;;
    apk)     run $SUDO apk add "$@" ;;
    *) die "couldn't find a package manager to install $*. Install it yourself, then run this again." ;;
  esac
}

# --- what it needs ----------------------------------------------------------------------------

step "Checking what the host needs"
missing=()
for tool in git curl; do
  if command -v "$tool" >/dev/null; then ok "$tool"; else warn "$tool isn't installed"; missing+=("$tool"); fi
done
if [ ${#missing[@]} -gt 0 ]; then
  confirm "Install ${missing[*]} now?" y || die "the host needs ${missing[*]}."
  pkg_install "${missing[@]}" ca-certificates
fi

docker_ok() { command -v docker >/dev/null && docker compose version >/dev/null 2>&1; }
if docker_ok; then
  ok "Docker $(docker version --format '{{.Client.Version}}' 2>/dev/null || true) with Compose $(docker compose version --short 2>/dev/null || true)"
else
  if command -v docker >/dev/null; then warn "Docker is here, but not its Compose plugin"; else warn "Docker isn't installed"; fi
  [ "$IN_WSL" = 1 ] && say "     (Docker Desktop on Windows works too: turn on WSL integration for this distro in its settings, then run this again.)"
  confirm "Install Docker Engine with Compose now, from Docker's own install script (get.docker.com)?" y \
    || die "the host runs in Docker. Install Docker Engine and the Compose plugin, then run this again."
  if [ "$PKG" = pacman ]; then
    pkg_install docker docker-compose
  elif [ "$DRY_RUN" = 1 ]; then
    say "  would run: curl -fsSL https://get.docker.com | sh"
  else
    curl -fsSL https://get.docker.com -o /tmp/get-docker.sh
    $SUDO sh /tmp/get-docker.sh
    rm -f /tmp/get-docker.sh
  fi
fi

# The Docker service has to be running, and should start by itself after a restart.
if [ "$DRY_RUN" = 0 ] && ! $SUDO docker info >/dev/null 2>&1; then
  say "  Starting the Docker service…"
  if [ -d /run/systemd/system ]; then
    $SUDO systemctl enable --now docker >/dev/null 2>&1 || true
  else
    $SUDO service docker start >/dev/null 2>&1 || true
  fi
  for _ in $(seq 20); do $SUDO docker info >/dev/null 2>&1 && break; sleep 1; done
  $SUDO docker info >/dev/null 2>&1 || die "Docker is installed but its service won't start. Try: $SUDO service docker start"
fi
[ "$DRY_RUN" = 1 ] || ok "Docker is running"

if [ "$IN_WSL" = 1 ] && [ ! -d /run/systemd/system ]; then
  warn "systemd is off in this WSL distro, so Docker won't start by itself after Windows restarts."
  if confirm "Turn systemd on in /etc/wsl.conf? (It takes effect after: wsl --shutdown)" y; then
    if [ "$DRY_RUN" = 0 ]; then
      if grep -q '^\[boot\]' /etc/wsl.conf 2>/dev/null; then
        $SUDO sed -i '/^\[boot\]/,/^\[/{/^systemd *=/d}' /etc/wsl.conf
        $SUDO sed -i 's/^\[boot\]/[boot]\nsystemd=true/' /etc/wsl.conf
      else
        printf '\n[boot]\nsystemd=true\n' | $SUDO tee -a /etc/wsl.conf >/dev/null
      fi
    fi
    ok "systemd turned on for the next WSL start"
  else
    say "     Until then, start Docker yourself after a restart: sudo service docker start"
  fi
fi

# Run docker as this user if allowed, otherwise with sudo.
DOCKER="docker"
docker info >/dev/null 2>&1 || DOCKER="$SUDO docker"

# --- the app ----------------------------------------------------------------------------------

step "Getting The Last Piece"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" 2>/dev/null && pwd || true)"
if [ -z "$INSTALL_DIR" ]; then
  if [ -n "$HERE" ] && [ -f "$HERE/server/docker-compose.yml" ]; then INSTALL_DIR="$HERE"
  else INSTALL_DIR="$(ask "Folder for the app" "$HOME/the-last-piece")"; fi
fi
if [ -f "$INSTALL_DIR/server/docker-compose.yml" ]; then
  ok "using $INSTALL_DIR"
  if [ -d "$INSTALL_DIR/.git" ] && confirm "Update it to the latest version first?" y; then
    run git -C "$INSTALL_DIR" pull --ff-only -q 2>/dev/null || warn "couldn't update it (changed files, or not on a branch); carrying on with this version"
  fi
else
  run git clone --depth 1 "$REPO_URL" "$INSTALL_DIR"
  ok "downloaded to $INSTALL_DIR"
fi
SERVER="$INSTALL_DIR/server"
ENV_FILE="$SERVER/.env"
dc() { (cd "$SERVER" && $DOCKER compose "$@"); }

# --- settings ---------------------------------------------------------------------------------

step "Settings"
rand() {
  local s=""
  while [ ${#s} -lt "$1" ]; do s="$s$(head -c 48 /dev/urandom | base64 | tr -dc 'A-Za-z0-9')"; done
  printf '%s' "${s:0:$1}"
}
getenv() { [ -f "$ENV_FILE" ] && sed -n "s/^$1=//p" "$ENV_FILE" | tail -1 || true; }
setenv() {
  local key="$1" value="$2"
  [ "$DRY_RUN" = 1 ] && { say "  would set $key"; return; }
  if grep -q "^$key=" "$ENV_FILE"; then
    local tmp; tmp="$(mktemp)"
    awk -v k="$key" -v v="$value" 'BEGIN{FS=OFS="="} $1==k {print k "=" v; next} {print}' "$ENV_FILE" > "$tmp"
    cat "$tmp" > "$ENV_FILE"; rm -f "$tmp"
  else
    printf '%s=%s\n' "$key" "$value" >> "$ENV_FILE"
  fi
}

FRESH=0
if [ ! -f "$ENV_FILE" ]; then
  FRESH=1
  run cp "$SERVER/.env.example" "$ENV_FILE"
  [ "$DRY_RUN" = 1 ] || chmod 600 "$ENV_FILE"
fi
if [ "$FRESH" = 1 ] || [ "$(getenv SYNCPLAY_PASSWORD)" = change-me ]; then
  setenv SYNCPLAY_PASSWORD "$(rand 20)"
  setenv SYNCPLAY_SALT "$(rand 20)"
  ok "made a new Syncplay password (viewers never type it; it travels in the session link)"
fi
TOKEN="$(getenv TLP_ADMIN_TOKEN)"
if [ -z "$TOKEN" ]; then TOKEN="$(rand 24)"; setenv TLP_ADMIN_TOKEN "$TOKEN"; ok "made an admin password for the host page"; fi

# The address other PCs use. In WSL that's the Windows PC's own network address.
lan_ip() {
  if [ "$IN_WSL" = 1 ] && command -v powershell.exe >/dev/null; then
    powershell.exe -NoProfile -NonInteractive -Command \
      "(Get-NetIPConfiguration | Where-Object { \$_.IPv4DefaultGateway -ne \$null -and \$_.NetAdapter.Status -eq 'Up' } | Select-Object -First 1).IPv4Address.IPAddress" \
      2>/dev/null | tr -d '\r' | head -1
  else
    ip -4 route get 1.1.1.1 2>/dev/null | sed -n 's/.* src \([0-9.]*\).*/\1/p' | head -1
  fi
}
PORT="$(getenv TLP_HTTP_PORT)"; PORT="${PORT:-8080}"
current_url="$(getenv TLP_PUBLIC_URL)"
if [ -z "$PUBLIC_URL" ]; then
  detected="$(lan_ip || true)"
  suggestion="$current_url"
  if [ -z "$suggestion" ] || [ "$suggestion" = "http://localhost:8080" ]; then
    suggestion="http://${detected:-localhost}:$PORT"
  fi
  say "  Other PCs open session links at this address. For PCs at home, use this computer's"
  say "  network address$([ "$IN_WSL" = 1 ] && printf ' (the Windows PC'"'"'s, not WSL'"'"'s)')${detected:+, which looks like $detected}."
  say "  For viewers outside your home, use your public IP or a domain, with ports $PORT and 8999 forwarded on the router."
  PUBLIC_URL="$(ask "Address other PCs use" "$suggestion")"
fi
case "$PUBLIC_URL" in http://*|https://*) ;; *) PUBLIC_URL="http://$PUBLIC_URL" ;; esac
setenv TLP_PUBLIC_URL "$PUBLIC_URL"
ok "address: $PUBLIC_URL"
case "$PUBLIC_URL" in *localhost*|*127.0.0.1*) warn "that address only works on this computer; other PCs won't get in. Run this again to change it." ;; esac

current_shelf="$(getenv SHELF_DIR)"
[ -z "$SHELF" ] && SHELF="$(ask "Folder with the episodes to share" "${current_shelf:-./shelf}")"
case "$SHELF" in /*) SHELF_ABS="$SHELF" ;; *) SHELF_ABS="$SERVER/${SHELF#./}" ;; esac
setenv SHELF_DIR "$SHELF"
run mkdir -p "$SHELF_ABS"
# The host app runs as user and group 1654 in its container, and needs to write there to clean up
# old episodes. You keep owning the folder so you can still copy files in (from Windows Explorer
# too); group 1654 gets write access, and new files and folders inside inherit that group.
if run $SUDO chown -R "$(id -u):1654" "$SHELF_ABS" 2>/dev/null && run $SUDO chmod -R g+rwX "$SHELF_ABS" \
   && run $SUDO find "$SHELF_ABS" -type d -exec chmod g+s {} +; then
  command -v setfacl >/dev/null && { run $SUDO setfacl -R -d -m g:1654:rwX "$SHELF_ABS" 2>/dev/null || true; }
else
  warn "couldn't give the app's group (1654) access to $SHELF_ABS; cleanup may not be able to delete old episodes"
fi
ok "episodes folder: $SHELF_ABS"
case "$SHELF_ABS" in /mnt/[a-z]/*) [ "$IN_WSL" = 1 ] && warn "a folder on a Windows drive works, but is slow from WSL and cleanup may not be able to delete there. A folder inside WSL is better." ;; esac

if [ -z "$GROUP" ] && [ -z "$(getenv TLP_GROUP_NAME)" ]; then GROUP="$(ask "Your group's name, shown as the title in everyone's app (Enter to skip)" "")"; fi
[ -n "$GROUP" ] && setenv TLP_GROUP_NAME "$GROUP"

# --- start ------------------------------------------------------------------------------------

port_busy() { (ss -ltnH "sport = :$1" 2>/dev/null || true) | grep -q .; }
if [ "$NO_START" = 0 ]; then
  step "Starting the host"
  running="$(dc ps -q host 2>/dev/null || true)"
  for p in "$PORT" 8999; do
    if [ -z "$running" ] && port_busy "$p"; then die "port $p is already in use on this machine. Stop what's using it (ss -ltnp | grep :$p), then run this again."; fi
  done
  say "  Building and starting (the first time takes a few minutes)…"
  run dc up -d --build
  if [ "$DRY_RUN" = 0 ]; then
    for _ in $(seq 120); do curl -fs "http://127.0.0.1:$PORT/healthz" >/dev/null 2>&1 && break; sleep 1; done
    curl -fs "http://127.0.0.1:$PORT/healthz" >/dev/null 2>&1 \
      || die "the host app didn't come up. See what happened with: cd $SERVER && $DOCKER compose logs host"
    ok "host app answers on port $PORT"
    for _ in $(seq 30); do (exec 3<>/dev/tcp/127.0.0.1/8999) 2>/dev/null && break; sleep 1; done
    if (exec 3<>/dev/tcp/127.0.0.1/8999) 2>/dev/null; then ok "Syncplay answers on port 8999"; else warn "Syncplay isn't answering on 8999 yet. Check: cd $SERVER && $DOCKER compose logs syncplay"; fi
  fi
fi

# --- Windows, for WSL -------------------------------------------------------------------------

if [ "$IN_WSL" = 1 ] && [ "$WIN_NET" != skip ]; then
  step "Letting other PCs in (Windows side)"
  say "  WSL sits behind Windows, so Windows has to pass ports $PORT and 8999 on to it, and"
  say "  Windows Firewall has to let them in. This needs one Windows admin prompt."
  build="$(cmd.exe /c ver 2>/dev/null | tr -d '\r' | sed -n 's/.*\.\([0-9]\{5\}\)\..*/\1/p' | head -1 || true)"
  mirrored_ok=0; [ "${build:-0}" -ge 22621 ] 2>/dev/null && mirrored_ok=1
  if [ -z "$WIN_NET" ]; then
    if [ "$mirrored_ok" = 1 ]; then
      say "  1) Mirrored networking (best: viewers can swap pieces with each other). Needs one WSL restart."
      say "  2) Port forwarding (works right away; re-run this after Windows restarts if a second PC can't connect)."
      say "  3) Skip (set it up yourself later)."
      case "$(ask "Pick 1, 2 or 3" 1)" in 1) WIN_NET=mirrored ;; 2) WIN_NET=portproxy ;; *) WIN_NET=skip ;; esac
    else
      say "  This Windows is older than Windows 11 22H2, so it uses port forwarding."
      confirm "Set up port forwarding and the firewall now?" y && WIN_NET=portproxy || WIN_NET=skip
    fi
  fi
  if [ "$WIN_NET" = mirrored ] && [ "$mirrored_ok" = 0 ]; then warn "mirrored networking needs Windows 11 22H2 or later; using port forwarding"; WIN_NET=portproxy; fi
  if [ "$WIN_NET" != skip ]; then
    wsl_ip="$(ip -4 -o addr show eth0 2>/dev/null | awk '{print $4}' | cut -d/ -f1 | head -1 || true)"
    win_temp="$(cmd.exe /c 'echo %TEMP%' 2>/dev/null | tr -d '\r' | tail -1)"
    win_profile="$(cmd.exe /c 'echo %USERPROFILE%' 2>/dev/null | tr -d '\r' | tail -1)"
    [ -n "$win_temp" ] || die "couldn't reach Windows from WSL (cmd.exe). Run server/windows-network.ps1 in Windows PowerShell as administrator instead."
    script_win="$win_temp\\tlp-windows-network.ps1"
    result_win="$win_temp\\tlp-windows-network.txt"
    if [ "$DRY_RUN" = 1 ]; then
      say "  would run server/windows-network.ps1 -Mode $WIN_NET as administrator"
    else
      cp "$SERVER/windows-network.ps1" "$(wslpath -u "$script_win")"
      rm -f "$(wslpath -u "$result_win")"
      say "  Windows will ask for permission. Choose Yes."
      powershell.exe -NoProfile -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','\"$script_win\"','-Mode','$WIN_NET','-WslAddress','$wsl_ip','-HttpPort','$PORT','-UserProfile','\"$win_profile\"','-ResultFile','\"$result_win\"'" \
        2>/dev/null || true
      result="$(tr -d '\r' < "$(wslpath -u "$result_win")" 2>/dev/null || true)"
      if printf '%s' "$result" | grep -q '^ok'; then
        setenv TLP_WSL_NETWORK "$WIN_NET"
        ok "Windows is set up ($WIN_NET)"
        dc up -d >/dev/null 2>&1 || true
      else
        warn "the Windows step didn't finish${result:+: $result}."
        say "     You can run it yourself: in Windows PowerShell as administrator,"
        say "     powershell -ExecutionPolicy Bypass -File \"$(wslpath -w "$SERVER/windows-network.ps1")\" -Mode $WIN_NET -WslAddress $wsl_ip"
      fi
    fi
  fi
fi

# --- done -------------------------------------------------------------------------------------

step "Ready"
say "  Host page:      http://localhost:$PORT  (on this PC)   or  $PUBLIC_URL"
say "  Admin password: $TOKEN"
say "  Episodes go in: $SHELF_ABS"
[ "$IN_WSL" = 1 ] && command -v wslpath >/dev/null && say "                  In Windows Explorer: $(wslpath -w "$SHELF_ABS" 2>/dev/null || true)"
say ""
say "  Next: open the host page, sign in, and look at the setup check at the top."
say "  Then make a session and open its link on another PC."
if [ "$WIN_NET" = mirrored ]; then
  say ""
  warn "One more step for mirrored networking: in Windows, run  wsl --shutdown  and open your"
  say "     WSL terminal again. The host starts by itself, and other PCs can reach it after that."
fi
say ""
say "  Update later:   bash $INSTALL_DIR/install.sh"
say "  Uninstall:      bash $INSTALL_DIR/uninstall.sh"
