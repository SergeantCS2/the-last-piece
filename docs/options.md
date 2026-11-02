# Options considered

Survey done 2026-11-02. Labels: **PROVEN** = checked in a source or tested here, **INFERRED** = reasoned from sources or general knowledge but not verified, **UNKNOWN** = not checked.

## The two models

**A. Host streams, friends watch.** the host's PC plays the file and sends encoded video to everyone.
**B. Everyone plays their own copy, playback is synced.** Only tiny control messages (play, pause, seek, chat) travel live.

The deciding fact: in model A every viewer receives a separate copy of the stream from the host's upload, so N viewers cost N times the bitrate (INFERRED, true of any self-hosted stream without a CDN). Model B costs nothing live if files are fetched ahead of time. And when friends stream the original file from the host's shelf, the upload cost is about the same as model A, but nothing is re-encoded and every subtitle and audio track survives.

| | A. Host streaming | B. Synced local playback |
|---|---|---|
| Picture quality | Capped by the host's upload ÷ viewers, re-encoded | Original file, bit for bit |
| ASS/SSA subtitles | Burned into the video, one style for all | Rendered locally by libass in mpv, each viewer toggles |
| Audio track choice | One track for all | Per viewer (JP/EN dub) |
| Volume, window size, fullscreen | Per viewer | Per viewer |
| Upload needed live | N × stream bitrate | ~0 (pre-download) or N × file bitrate (shelf streaming) |
| Friend setup | Open a link | Install Syncplay + mpv once, get the file |
| Latency between viewers | WebRTC well under a second (INFERRED) | Syncplay keeps viewers within its desync thresholds, seconds at worst (INFERRED) |

## Off-the-shelf tools

| Tool | What it is | Fit | Notes |
|---|---|---|---|
| **Syncplay** | Sync server + client that drives mpv, MPC-HC/BE, VLC, IINA | **Primary pick** | 1.7.6 released 2026-08-04 (PROVEN). Shared playlists, rooms, readiness, TLS (PROVEN). Chat typed in mpv and drawn over the video, in "Chatroom" or "Scrolling" style (PROVEN, client guide). Files don't need to be identical if durations match (PROVEN, client guide). Server tested here in Docker: join, pause propagation and chat all work (PROVEN, `tools/smoketest.py`). |
| **Synkplay** (formerly Syncplay Mobile) | Unofficial Syncplay client for Android and iOS | Phone viewers | mpv/libass engine by default, shared playlists, on Play Store and App Store (PROVEN, project README). Not tested here. |
| **Jellyfin SyncPlay** | Media server with group watch | Considered, not picked | Works in web-wrapper clients, not the Android TV app; transcoding can cause drift (PROVEN, Android Authority). Long-running reports of ASS/SRT subtitle desync after seeking in web clients (PROVEN, jellyfin-web issue 4346). It would also mean running a full media server and remote access for it. Worth revisiting if the host already runs Jellyfin. |
| **Plex Watch Together** | Plex's group watch | Ruled out | Dropped from Plex's new apps, web only (PROVEN, AlternativeTo / PCWorld). |
| **OBS → MediaMTX → browser** | Self-hosted WebRTC streaming | **Fallback** | OBS has had WHIP output since 30.0, simulcast since 32.1 (PROVEN, obs-versions.com). MediaMTX accepts OBS and serves WebRTC (PROVEN at the "supports WebRTC" level; WHIP/WHEP specifics and codecs UNKNOWN). Good for a friend who can't install anything. |
| **Owncast** | Self-hosted Twitch-style stream | Weaker fallback | HLS adds several seconds of delay; fine for a one-way show, worse for "pause, everyone talk" (INFERRED). |
| **Galène / LiveKit** | WebRTC conferencing / SFU | Overkill | Built for many-to-many calls; Discord already covers voice (INFERRED). |
| **Sunshine / Moonlight** | Low-latency game streaming | Ruled out | Made for one remote player controlling a PC, not a watch party (INFERRED; multi-viewer behaviour UNKNOWN). |
| **Watch2Gether, Hyperbeam, Kosmi** | Hosted watch-party sites | Ruled out | Built around web video or a cloud browser, not local MKV files with full subtitle styling (INFERRED). |
| **JASSUB** | libass compiled to WebAssembly for browsers | Building block for a later web client | Full libass feature set incl. embedded fonts, MIT licensed (PROVEN, README). Lets a browser player render ASS properly. |

## Why not build from scratch

Syncplay already solves the hard part (keeping players in step across network jitter, seeking, readiness, reconnects) and is actively maintained (five releases since April 2024). Rebuilding that would cost months and come out worse. The useful work is the glue around it: hosting, getting files to friends, one-click setup, Discord integration, and maybe a browser client later.

## Sources

- Syncplay news and releases: https://syncplay.pl/news/
- Syncplay server guide: https://syncplay.pl/guide/server/
- Syncplay client guide: https://syncplay.pl/guide/client/
- Synkplay: https://github.com/yuroyami/syncplay-mobile
- Jellyfin SyncPlay explainer: https://www.androidauthority.com/jellyfin-syncplay-explained-3530437/
- Jellyfin subtitle desync issue: https://github.com/jellyfin/jellyfin-web/issues/4346
- Plex Watch Together: https://alternativeto.net/news/2025/2/plex-ends-support-for-watch-together-feature/
- OBS WHIP status: https://obs-versions.com/blog/obs-webrtc-whip-streaming
- MediaMTX: https://github.com/bluenviron/mediamtx
- JASSUB: https://github.com/ThaUnknown/jassub
