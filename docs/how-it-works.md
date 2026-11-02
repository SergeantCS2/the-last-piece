# How it works

The design in more detail than the [README](../README.md): how files get to everyone, how the
peer-to-peer part works, and why each piece was chosen. [options.md](options.md) covers the
alternatives that were considered.

Labels used here: **PROVEN** (tested, or confirmed in a source), **INFERRED** (reasoned, not
verified), **UNKNOWN**.

## Overview

```
                               Voice chat (Discord or anything else, unchanged)

 ┌──────────────────────── Host (a PC in WSL, or a Linux server) ──────────────────────┐
 │  Host app (:8080, HTTPS via Caddy)              Syncplay server (:8999)             │
 │  - admin page: shelf, nights, cleanup           - rooms, shared playlist            │
 │  - movie night link → pack.json + web page      - play/pause/seek sync              │
 │  - files served with resume                     - on-screen chat (mpv)              │
 │  - "find magnets on this page"                                                      │
 └───────────▲───────────────────────────────────────────────▲──────────────────────────┘
             │ pack + shelf downloads                         │ Syncplay protocol
             │                                                │
 ┌───────────┴───────────────────────────────┐   ┌───────────┴───────────────┐
 │ Windows viewer app                         │   │ Phone: Synkplay            │
 │ 1. tlp:// link → reads the pack            │   │ (third-party, untested)    │
 │ 2. each file: torrent swarm or host copy   │   └───────────────────────────┘
 │    → size + SHA-256 check against host     │
 │ 3. Join → bundled Syncplay + mpv           │◀──── torrent swarm (public, and
 └────────────────────────────────────────────┘      friends seeding to each other)
```

## Movie night pack

One link per night. The host app turns it into a page with an "Open in The Last Piece" button, and into `pack.json` for the app:

- Title and start time.
- Syncplay server, room and password, so friends never type them.
- For each episode:
  - The exact filename. Syncplay's shared playlist matches files by name (PROVEN, client guide).
  - Size, and SHA-256 if the host has the file.
  - Sources: a shelf download link, a magnet, or both.

## Getting files

| Source | When | Cost to the host |
|---|---|---|
| **Group torrent** (built in) | The host has the file on the shelf. Made automatically. | About one copy's worth, shared out: friends swap pieces, the host's shelf fills the gaps. |
| **Public magnet** (built in) | The episode came from a public torrent. The host pastes the magnet, or pastes the page and the host app finds it. | None: friends download from the public swarm. |
| **Host's copy** (plain download) | Fallback, or for anyone who prefers it. | One copy per friend. |

**Get to my server.** The host app can download a public magnet onto its own shelf first ("Get to my server" next to a found magnet, or the tick box when creating a night). When it finishes, every night that lists that magnet switches to the host's copy by itself, so friends get it from the host's server: the group torrent (the host's own private "magnet") plus the plain download, with the public magnet left as a last resort. Half-downloaded files stay out of the shelf until they're complete, it resumes after a restart, and it stops sharing with the public swarm once done. PROVEN in a test with a real torrent transfer through the host app; INFERRED for public swarms and real home networks.

- **Picking a source.** Each friend chooses in settings:
  - Best available (default): group torrent, then the host's copy, then the public magnet.
  - Torrents first: group torrent, then public magnet, then the host's copy.
  - Host's copy first.
- **Fallback.** If a source fails, or a torrent gets no new data for 3 minutes, the app moves to the next one. PROVEN in a test with a broken torrent link.
- **Verifying.** After any download, the app checks size and SHA-256 against the host's copy, so a different release with the same name is caught. Without a hash it checks size only.
  - PROVEN: unit tests cover this, including a deliberately wrong file.
  - INFERRED: the check only runs when the host has the file on the shelf. A magnet-only entry gets a size check only.
- **Seeding while open.** While the app is open, finished torrent files keep seeding, so friends who started late get pieces from friends, not just strangers (on by default, can be turned off).

## Peer to peer

- **Group torrent, for files on the host's shelf:**
  - The host app makes a private `.torrent` for each shelf file in a night, in the background.
  - It runs the tracker itself, behind the night's secret link, so only people with the link find each other. Private means clients don't use DHT or peer exchange, so strangers can't join.
  - The shelf link is the web seed: The host's copy is always there to fill gaps, without a torrent client on the host.
  - PROVEN, with the real host app over real sockets:
    - Friend A downloads with nobody else around, entirely from the web seed.
    - The host's file is then deleted, and friend B still gets the whole file from friend A through the tracker.
- **Public magnets** download inside the app too, and finished files keep seeding while the app is open. PROVEN with two local peers.
- **Where it can fall short (untested on real home networks):**
  - The tracker hands out the address each friend connects from. If the host app runs behind something that hides those addresses, friends can't find each other and everyone pulls from the web seed instead.
    - Hiding happens with Docker Desktop or WSL port forwarding on Windows (UNKNOWN for a given setup), and with a reverse proxy unless `TLP_BEHIND_PROXY=true`.
    - Downloads still work either way, they just don't save the host's upload.
  - Two friends can swap pieces only if at least one of them can accept connections. The app asks the router via UPnP; routers without it limit sharing to friends who can.
- **Sync stays on the Syncplay server.** Peer-to-peer sync without a server is possible but would mean rewriting Syncplay for little gain.

## Why not uTorrent

- uTorrent is closed source and has a history of bundled adware and, in 2015, a bundled cryptocurrency miner (INFERRED: widely reported, not re-verified here).
- The app's built-in engine is MonoTorrent: MIT licensed, open source, and only downloads what's in the pack.
- Anyone who wants an external client can still copy the magnet from the night page. qBittorrent is the open-source pick there.
- **Note:** a torrent shows your IP address to everyone in the swarm, as with any torrent client.

## The player: bundled mpv with a VLC look

- Syncplay supports VLC 3, so anyone can keep VLC (PROVEN, Syncplay player list). Chat over the video is mpv-only (PROVEN, Syncplay client guide).
- Styled anime subtitles: VLC 3 and mpv both use libass, so they should look the same (INFERRED: same library, versions differ).
- Plain subtitles: `viewer/mpv.conf` approximates VLC 3's Windows default (Arial, white, black outline, soft shadow). INFERRED until compared side by side.
- mpv reads `mpv/portable_config/mpv.conf` next to `mpv.exe` (PROVEN, mpv docs), so the bundled player doesn't touch anyone's own mpv setup.

## Hosting and cleanup

| How | Host app | Syncplay server | Status |
|---|---|---|---|
| Docker (Linux server, or WSL on Windows) | `server/docker-compose.yml` | same compose file | PROVEN: both images built and tested here |
| Native Linux | `dotnet publish` binary | Syncplay from source, started by the host app (`Tlp__Syncplay__ServerCommand`) | INFERRED |
| Native Windows | `dotnet publish` `.exe` | `syncplayServer.exe` from the Syncplay download, started by the host app | INFERRED |

Cleanup:

- A shelf file is deleted a set number of days (default 7) after the last movie night that uses it, unless pinned.
- Files no night uses are never deleted automatically. The admin page lists them with a delete button and shows disk space.
- Cleanup has a preview, and can be switched to report-only (`Tlp__CleanupEnabled=false`).
- PROVEN: logic unit-tested, and delete works in Docker.
- Docker detail: the host app runs as user id 1654. The shelf folder has to be writable by it for cleanup (for example `sudo chown -R 1654 shelf`).

## Room helper and "Who's ready"

- Room helper: fills each active night's Syncplay room playlist, so the room goes to the next episode together. PROVEN against real Syncplay 1.7.6 (locally and in the `server` CI run).
  - It only steps into a room while it's empty, because Syncplay pauses a room when anyone leaves it, the helper included. "Fill room playlist now" on the admin page can force it, which pauses people in the room once.
  - It never sends a play position and has no file open, so Syncplay leaves it out of "wait for the slowest viewer" and of the ready count (PROVEN in Syncplay's source).
  - Turn it off with `TLP_ROOM_HELPER=false`.
- "Who's ready" on each night: per friend, files done out of total, app open or away, in the room, and what they're playing. PROVEN with tests and real Syncplay; the viewer side is INFERRED on Windows.

## No Syncplay connect window

- The app writes a `syncplay.ini` next to the bundled `Syncplay.exe`. Syncplay 1.7.6 reads a file there instead of the user's own settings (PROVEN in Syncplay's source, `_checkForPortableFile`). It sets `forceGuiPrompt = False` and turns off update pop-ups, so the bundled Syncplay doesn't touch anyone's own Syncplay setup.
- If the app folder is read-only, the window appears again, filled in.
- CI checks this on Windows: the bundled Syncplay has to join a real server with the file open, which it can't do if the window stopped it.

## Notes on Syncplay

- Syncplay isn't on PyPI, so the image installs from the tagged GitHub release (PROVEN).
- `--isolate-rooms` silently disables the saved-rooms database in 1.7.6 (PROVEN). Isolation is opt-in with `SYNCPLAY_ISOLATE_ROOMS=true`.
- Docker's port proxy accepts connections before Syncplay listens, so the smoke test waits for a real handshake.

## Download size

The Windows zip is about 88 MB (it was about 120 MB before trimming). Each viewer downloads it once. Sizes are from the CI build, zipped:

| Part | Zipped | Why it's there | Could it shrink? |
|---|---|---|---|
| mpv | 45.9 MB | The player. `mpv.exe` alone is 116 MB unzipped because the video decoders (FFmpeg) and subtitle renderer (libass) are built into it. | Only with a different mpv build. Not worth the risk for now. |
| Syncplay | 25.7 MB | Keeps everyone in step. It's a Python program packed with Python and the Qt window toolkit. | Dropping unused Qt parts might save a few MB (INFERRED, risky). Replacing Syncplay with our own sync code would save it all but means rebuilding the hardest part. Not worth it now. |
| The Last Piece | 16.3 MB | Our app, with .NET included so nobody installs anything else. | Already trimmed: unused .NET code is cut out at build time. |

Already removed: mpv's installer and updater scripts. For comparison, one 1080p anime episode is usually 1 to 1.5 GB, so the zip is well under the size of a single episode (INFERRED, typical release sizes).
