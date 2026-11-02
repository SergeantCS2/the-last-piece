# Setup guide

Two parts: the **host** (one person, usually the one with the files) and the **viewer app**
(everyone watching, on Windows). Phones can join with Synkplay; see the end.

## 1. Host

The host runs two things side by side: the host app (admin page, shelf of episodes, movie night
links, group torrents, cleanup) and a Syncplay server (keeps everyone's players in step).

### Docker (Linux server, or WSL on Windows)

Needs Docker with Compose.

```bash
git clone https://github.com/SergeantCS2/the-last-piece
cd the-last-piece/server
cp .env.example .env        # then edit it, see below
mkdir -p shelf              # episodes go here
sudo chown 1654 shelf       # the host app runs as user 1654 and needs to delete old episodes
docker compose up -d --build
docker compose logs host | grep -i token
```

The last line prints the admin token for the first sign-in (it's also saved as
`admin-token.txt` in the data volume). Open `http://<this machine>:8080` and sign in. The page opens with a setup check that flags anything friends would trip over (a localhost or home-only address, Syncplay not answering, missing files), with what to do about each.

Settings in `server/.env` that matter on day one:

| Setting | What to put |
|---|---|
| `SYNCPLAY_PASSWORD` | Any password. Friends never type it; it travels inside the movie night link. |
| `TLP_PUBLIC_URL` | How friends reach this machine, e.g. `http://203.0.113.7:8080` or `https://movies.example.com`. |
| `SHELF_DIR` | The folder with your episodes, if not `./shelf`. |
| `TLP_RETENTION_DAYS` | Days to keep an episode after its last night (pin to keep it longer). |

Everything else in `.env.example` is explained there and can wait.

### Native (no Docker)

```bash
dotnet publish src/Tlp.Host -c Release -r linux-x64 -o out    # or -r win-x64
```

Run `out/Tlp.Host` next to a Syncplay server you start yourself (`syncplay-server --port 8999
--password ...`). Settings go in `appsettings.json` or `Tlp__...` environment variables; every
option is in `src/Tlp.Host/TlpOptions.cs`.

### Ports

| Port | For | Notes |
|---|---|---|
| 8999/TCP | Syncplay | Forward it on your router. If your domain is on Cloudflare, that record must be DNS-only (grey cloud). |
| 8080/TCP | Host app | Fine for a first test. |
| 6890/TCP+UDP | Get to my server | Optional. Forwarding it makes the server's own torrent downloads faster. |
| 80, 443/TCP | Host app over HTTPS | `docker compose --profile https up -d` with `TLP_DOMAIN` set and `TLP_BEHIND_PROXY=true`. |

Check Syncplay from anywhere with Python 3:

```bash
python3 tools/smoketest.py --host <your address> --password '<password>' --room movienight
```

### A movie night

1. Put the episodes in the shelf folder, or paste magnets or a release page in the admin page.
   With **Get to my server** (or the tick box under the picked files), your server downloads the
   magnet itself. Once it's on your shelf, the night hands friends your copy and your private
   group torrent instead of the public swarm. Progress shows under **Downloads to your server**.
2. **New movie night**: pick the files, a title and a start time.
3. Copy the link, or press **Post to Discord** if you set a webhook.
4. Before the start, open **Who's ready** to see who has the files and who's in the room.

The host fills the Syncplay room's playlist with the night's episodes by itself, so everyone
moves to the next episode together.

## 2. Viewer app (Windows)

1. Run `TheLastPiece-win-Setup.exe` (from the host or the project's Releases page). It installs
   for you only, with no admin prompt. Until the app is signed, Windows may say it protected
   your PC: choose **More info**, then **Run anyway**.
2. Open **Settings** in the app: your name (what others see in the room) and where to keep
   movie night files.
3. Click the movie night link, then **Open in The Last Piece**. Or paste it into the app.
4. **Download all**. Files come from friends and the host by torrent, or straight from the host,
   and each one is checked against the host's copy.
5. At showtime, **Join movie night**. The player opens in the room.

In the player:

| Key | Does |
|---|---|
| Right-click | Menu |
| S / A | Pick subtitles / audio |
| V / B | Next subtitle / audio track (like VLC) |
| Mouse wheel, Up/Down | Volume, up to 300% (limit in Settings) |
| Enter | Chat on everyone's screen |
| Space | Pause for everyone |

Updates download in the background and install the next time the app opens.

### Running in WSL on Windows

Docker inside WSL 2 sits behind Windows, so friends reach Windows first and Windows has to pass
the connection on. Two ways (INFERRED from Microsoft's WSL networking docs; not tried on your PC yet):

- **Mirrored networking (Windows 11 22H2 or later), best.** WSL then shares Windows' own network,
  so ports 8080 and 8999 open on Windows directly and friends' real addresses reach the host,
  which friend-to-friend sharing needs. Put this in `%UserProfile%\.wslconfig`, then run
  `wsl --shutdown` and start WSL again:

  ```ini
  [wsl2]
  networkingMode=mirrored
  ```

- **Port proxy, works anywhere.** In an admin PowerShell on Windows, with the WSL address from
  `wsl hostname -I`:

  ```powershell
  netsh interface portproxy add v4tov4 listenport=8080 listenaddress=0.0.0.0 connectport=8080 connectaddress=<WSL address>
  netsh interface portproxy add v4tov4 listenport=8999 listenaddress=0.0.0.0 connectport=8999 connectaddress=<WSL address>
  ```

  Every friend then looks like the same Windows address to the host, so friends can't share
  pieces with each other. Downloads still work from your copy. The WSL address can change after a
  restart.

Either way, Windows Firewall has to let 8080 and 8999 in, and the router forwards them to the
Windows PC. The admin page's setup check warns when friends all show up as one inside address.

## 3. Phones

**Synkplay** (Android, iOS) can join the same server and room. Open the night link on the phone:
its "On a phone" part has the downloads and the server, port, room and password, each with a
Copy button. Use Synkplay's mpv engine for proper anime subtitles. Not tested yet.

## Troubleshooting

| Problem | Try |
|---|---|
| Friends can't connect to Syncplay | Port 8999 forwarded? Run the smoke test above from outside your network. |
| Downloads only come from the host | Normal when only one person has the file. Group torrents need two people downloading at once, or a friend who finished. |
| The setup check says friends show up as an inside address | Docker or WSL is hiding their real addresses, so they can't share pieces. See "Running in WSL on Windows", or on a Linux server run the host with `network_mode: host`. |
| The app says it can't reach the room server | Port 8999 isn't reachable: Syncplay isn't running, or the port isn't forwarded. The host's setup check shows which. |
| Cleanup doesn't delete anything | The shelf folder must be writable by user 1654 (`sudo chown 1654 shelf`). |
| "Who's ready" shows nobody in the room | `TLP_ROOM_HELPER` must be on, and Syncplay must not run with `--isolate-rooms`. |
