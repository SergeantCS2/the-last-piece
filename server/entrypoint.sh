#!/bin/sh
# Builds the syncplay-server command line from environment variables so the
# compose file stays readable. SYNCPLAY_PASSWORD and SYNCPLAY_SALT are read
# by Syncplay itself straight from the environment.
set -eu

set -- python3 /opt/syncplay/syncplayServer.py \
  --port "${SYNCPLAY_PORT:-8999}"

# Room isolation hides other rooms' viewers. Syncplay 1.7.6 ignores the rooms
# database when isolation is on, so it's off by default here: a private,
# password-protected server for one friend group doesn't need it, and
# persistent rooms are more useful.
if [ "${SYNCPLAY_ISOLATE_ROOMS:-false}" = "true" ]; then
  set -- "$@" --isolate-rooms
fi

# Persistent rooms keep the shared playlist between sessions and restarts.
if [ "${SYNCPLAY_ISOLATE_ROOMS:-false}" != "true" ] && [ -n "${SYNCPLAY_ROOMS_DB:-}" ]; then
  set -- "$@" --rooms-db-file "$SYNCPLAY_ROOMS_DB"
  if [ -n "${SYNCPLAY_PERMANENT_ROOMS:-}" ] && [ -f "$SYNCPLAY_PERMANENT_ROOMS" ]; then
    set -- "$@" --permanent-rooms-file "$SYNCPLAY_PERMANENT_ROOMS"
  fi
fi

if [ -n "${SYNCPLAY_MOTD_FILE:-}" ] && [ -f "$SYNCPLAY_MOTD_FILE" ]; then
  set -- "$@" --motd-file "$SYNCPLAY_MOTD_FILE"
fi

# Folder holding privkey.pem, cert.pem and chain.pem (certbot layout).
if [ -n "${SYNCPLAY_TLS_DIR:-}" ] && [ -f "$SYNCPLAY_TLS_DIR/cert.pem" ]; then
  set -- "$@" --tls "$SYNCPLAY_TLS_DIR"
fi

if [ -n "${SYNCPLAY_STATS_DB:-}" ]; then
  set -- "$@" --stats-db-file "$SYNCPLAY_STATS_DB"
fi

exec "$@"
