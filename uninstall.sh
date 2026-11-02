#!/usr/bin/env bash
# The Last Piece: removes the host that install.sh set up.
#
# Stops and removes the host app and Syncplay containers and their images, and asks before
# deleting anything else: the app's data (movie nights, admin password, Syncplay rooms), your
# episodes folder, the app folder, and (in WSL) the Windows network settings the installer made.
# Docker and git stay installed, since other things may use them.
#
#   bash uninstall.sh
#
# Options:
#   --yes            take the default answer everywhere: remove the app, its data and its folder,
#                    undo the Windows settings, and KEEP your episodes
#   --delete-shelf   also delete the episodes folder (with --yes)
#   --dir DIR        the app folder (default: the folder this script is in)
#   --dry-run        say what would happen, change nothing
#   -h, --help       this help

set -euo pipefail

ASSUME_YES=0 DRY_RUN=0 DELETE_SHELF=0 INSTALL_DIR=""

if [ -t 1 ]; then B=$'\033[1m' G=$'\033[32m' Y=$'\033[33m' R=$'\033[31m' N=$'\033[0m'; else B="" G="" Y="" R="" N=""; fi
say()  { printf '%s\n' "$*"; }
step() { printf '\n%s== %s%s\n' "$B" "$*" "$N"; }
ok()   { printf '  %sok%s  %s\n' "$G" "$N" "$*"; }
warn() { printf '  %s!!%s  %s\n' "$Y" "$N" "$*"; }
die()  { printf '\n%sStopped:%s %s\n' "$R" "$N" "$*" >&2; exit 1; }
run()  { if [ "$DRY_RUN" = 1 ]; then say "  would run: $*"; else "$@"; fi; }

while [ $# -gt 0 ]; do
  case "$1" in
    --yes|-y) ASSUME_YES=1 ;;
    --delete-shelf) DELETE_SHELF=1 ;;
    --dir) INSTALL_DIR="${2:?--dir needs a folder}"; shift ;;
    --dry-run) DRY_RUN=1 ;;
    -h|--help) sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "unknown option $1 (see --help)" ;;
  esac
  shift
done

TTY=/dev/tty
if [ "$ASSUME_YES" = 0 ] && ! { [ -r "$TTY" ] && : < "$TTY"; } 2>/dev/null; then
  die "no keyboard to ask questions on. Run it in a terminal, or add --yes."
fi
confirm() {
  local q="$1" def="${2:-y}" ans hint="[Y/n]"
  [ "$def" = n ] && hint="[y/N]"
  if [ "$ASSUME_YES" = 1 ]; then [ "$def" = y ]; return; fi
  printf '  %s %s ' "$q" "$hint" > "$TTY"
  IFS= read -r ans < "$TTY" || true
  case "${ans:-$def}" in [Yy]*) return 0 ;; *) return 1 ;; esac
}

[ -n "$INSTALL_DIR" ] || INSTALL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
SERVER="$INSTALL_DIR/server"
[ -f "$SERVER/docker-compose.yml" ] || die "$INSTALL_DIR doesn't look like The Last Piece (no server/docker-compose.yml). Use --dir."
ENV_FILE="$SERVER/.env"
getenv() { [ -f "$ENV_FILE" ] && sed -n "s/^$1=//p" "$ENV_FILE" | tail -1 || true; }

SUDO=""; [ "$(id -u)" = 0 ] || SUDO="sudo"
IN_WSL=0; grep -qi microsoft /proc/version 2>/dev/null && IN_WSL=1

SHELF="$(getenv SHELF_DIR)"; SHELF="${SHELF:-./shelf}"
case "$SHELF" in /*) SHELF_ABS="$SHELF" ;; *) SHELF_ABS="$SERVER/${SHELF#./}" ;; esac
WIN_NET="$(getenv TLP_WSL_NETWORK)"

say "This removes The Last Piece host from $INSTALL_DIR."
confirm "Go ahead?" y || { say "Nothing changed."; exit 0; }

# --- containers -------------------------------------------------------------------------------

step "Stopping the host"
if command -v docker >/dev/null; then
  DOCKER="docker"; docker info >/dev/null 2>&1 || DOCKER="$SUDO docker"
  dc() { (cd "$SERVER" && $DOCKER compose --profile https "$@"); }
  down=(down --remove-orphans --rmi all)
  if confirm "Delete the app's data too (movie nights, the admin password, Syncplay rooms)?" y; then
    down+=(--volumes)
    DATA_GONE=1
  else
    DATA_GONE=0
  fi
  if [ -f "$ENV_FILE" ]; then run dc "${down[@]}" || warn "Docker couldn't remove everything; see the message above"
  else warn "no server/.env, so nothing was started from here"; fi
  ok "containers and images removed$([ "$DATA_GONE" = 1 ] && printf ', with their data')"
else
  warn "Docker isn't installed, so there's nothing to stop"
fi

# --- Windows side (WSL) -----------------------------------------------------------------------

if [ "$IN_WSL" = 1 ] && [ -f "$SERVER/windows-network.ps1" ]; then
  step "Windows network settings"
  if [ -n "$WIN_NET" ] || [ "$ASSUME_YES" = 0 ]; then
    if confirm "Undo the Windows firewall rules, port forwards and WSL network setting the installer made? (One Windows admin prompt)" y; then
      win_temp="$(cmd.exe /c 'echo %TEMP%' 2>/dev/null | tr -d '\r' | tail -1)"
      win_profile="$(cmd.exe /c 'echo %USERPROFILE%' 2>/dev/null | tr -d '\r' | tail -1)"
      if [ -z "$win_temp" ]; then
        warn "couldn't reach Windows from WSL. In Windows PowerShell as administrator run: powershell -ExecutionPolicy Bypass -File windows-network.ps1 -Undo"
      elif [ "$DRY_RUN" = 1 ]; then
        say "  would run windows-network.ps1 -Undo as administrator"
      else
        script_win="$win_temp\\tlp-windows-network.ps1"; result_win="$win_temp\\tlp-windows-network.txt"
        cp "$SERVER/windows-network.ps1" "$(wslpath -u "$script_win")"
        rm -f "$(wslpath -u "$result_win")"
        say "  Windows will ask for permission. Choose Yes."
        powershell.exe -NoProfile -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','\"$script_win\"','-Undo','-UserProfile','\"$win_profile\"','-ResultFile','\"$result_win\"'" 2>/dev/null || true
        result="$(tr -d '\r' < "$(wslpath -u "$result_win")" 2>/dev/null || true)"
        if printf '%s' "$result" | grep -q '^ok'; then ok "${result#ok: }"; else warn "the Windows step didn't finish${result:+: $result}"; fi
        rm -f "$(wslpath -u "$script_win")" "$(wslpath -u "$result_win")"
      fi
    fi
  fi
fi

# --- files ------------------------------------------------------------------------------------

step "Files"
shelf_inside=0
case "$SHELF_ABS/" in "$INSTALL_DIR"/*) shelf_inside=1 ;; esac
if [ -d "$SHELF_ABS" ]; then
  size="$(du -sh "$SHELF_ABS" 2>/dev/null | cut -f1 || true)"
  def=n; [ "$DELETE_SHELF" = 1 ] && def=y
  if confirm "Delete your episodes folder $SHELF_ABS (${size:-?})?" "$def"; then
    run $SUDO rm -rf "$SHELF_ABS"
    ok "episodes deleted"
  elif [ "$shelf_inside" = 1 ]; then
    keep="$HOME/the-last-piece-episodes"
    [ -e "$keep" ] && keep="$keep-$(date +%Y%m%d%H%M%S)"
    run $SUDO mv "$SHELF_ABS" "$keep"
    run $SUDO chown -R "$(id -u):$(id -g)" "$keep"
    ok "episodes kept, moved out of the app folder to $keep"
  else
    ok "episodes kept in $SHELF_ABS"
  fi
fi

if confirm "Delete the app folder $INSTALL_DIR?" y; then
  case "$INSTALL_DIR" in /|"$HOME"|/home|/root|/usr|/etc) die "refusing to delete $INSTALL_DIR" ;; esac
  cd /
  run $SUDO rm -rf "$INSTALL_DIR"
  ok "app folder deleted"
else
  ok "app folder kept"
fi

step "Done"
say "  The Last Piece host is removed. Docker and git are still installed; remove them with your"
say "  package manager if nothing else needs them."
[ "$WIN_NET" = mirrored ] && say "  WSL goes back to its usual networking after: wsl --shutdown"
say "  The viewer app on Windows uninstalls from Settings, Apps, The Last Piece."
