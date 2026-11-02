# Setup guide

Two parts: the **host** (one person, usually the one with the files) and the **viewer app**
(everyone watching, on Windows). Phones can join with Synkplay; see the end.

## 1. Host

The host runs two things side by side: the host app (admin page, shelf of episodes, movie night
links, group torrents, cleanup) and a Syncplay server (keeps everyone's players in step).

### The installer (recommended)

On a Linux server, or in WSL on Windows (Ubuntu or any distro with apt, dnf or pacman):

```bash
curl -fsSL https://raw.githubusercontent.com/SergeantCS2/the-last-piece/main/install.sh | bash
```

It walks you through everything:

1. Checks for git, curl and Docker with Compose, and offers to install what's missing (Docker
   from get.docker.com). In WSL it starts Docker, and offers to turn on systemd so Docker starts
   with WSL.
2. Downloads the app to `~/the-last-piece` (or uses the folder it's already in).
3. Asks for the address viewers use (in WSL it suggests the Windows PC's network address), the
   episodes folder and your group's name. It makes the Syncplay and admin passwords and writes
   `server/.env`.
4. Starts the host and waits until the host page and Syncplay answer.
5. In WSL, offers to open Windows to the other PCs: firewall rules plus mirrored networking or
   port forwarding, with one Windows admin prompt (see [Running in WSL](#running-in-wsl-on-windows)).
6. Prints the host page address, the admin password and the episodes folder (with its Windows
   Explorer path in WSL).

Run it again any time to update: it pulls the latest version and keeps your settings and
passwords. Options for scripts: `--yes` (take the defaults), `--public-url`, `--shelf`,
`--group`, `--windows-network mirrored|portproxy|skip`, `--no-start`, `--dry-run`. See
`bash install.sh --help`.

### Uninstall

```bash
bash ~/the-last-piece/uninstall.sh
```

It stops and removes the containers and images and asks before deleting anything else: the app's
data (movie nights, admin password), the Windows network settings the installer made (in WSL, one
admin prompt), your episodes (kept by default, moved to `~/the-last-piece-episodes` if they were
inside the app folder) and the app folder. Docker and git stay installed. `--yes` takes the
defaults; add `--delete-shelf` to delete the episodes too.

### Docker by hand

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

The last line prints the admin password for the first sign-in (it's also saved as
`admin-token.txt` in the data volume). Open `http://<this machine>:8080` and sign in. The page opens with a setup check that flags anything viewers would trip over (a localhost or home-only address, Syncplay not answering, missing files), with what to do about each.

Settings in `server/.env` that matter on day one:

| Setting | What to put |
|---|---|
| `SYNCPLAY_PASSWORD` | Any password. Viewers never type it; it travels inside the movie night link. |
| `TLP_PUBLIC_URL` | How viewers reach this machine, e.g. `http://203.0.113.7:8080` or `https://movies.example.com`. |
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
   magnet itself. Once it's on your shelf, the night hands viewers your copy and your private
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
2. The first time it opens, a **Welcome** box asks for your name (what others see in the room),
   where to keep movie night files, a look, and the movie night link if you have one. If Windows
   asks whether the app may use the network, choose **Allow**. All of it can be changed later in
   **Settings**.
3. Click the movie night link, then **Open in The Last Piece**. Or paste it into the app.
4. **Download all**. Files come from the other viewers and the host by torrent, or straight from the host,
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

Docker inside WSL 2 sits behind Windows, so other PCs reach Windows first and Windows has to pass
the connection on. The installer does this for you with `server/windows-network.ps1`, which you
can also run yourself in an admin PowerShell (`-Mode Mirrored`, `-Mode PortProxy -WslAddress
<address>`, or `-Undo` to remove everything it added). It adds Windows Firewall rules for 8080,
8999 and 6890 (group "The Last Piece") and sets up one of these two ways, which come from
Microsoft's WSL networking docs:

- **Mirrored networking (Windows 11 22H2 or later), best.** WSL then shares Windows' own network,
  so ports 8080 and 8999 open on Windows directly and viewers' real addresses reach the host,
  which sharing between viewers needs. Put this in `%UserProfile%\.wslconfig`, then run
  `wsl --shutdown` and start WSL again:

  ```ini
  [wsl2]
  networkingMode=mirrored

  [experimental]
  hostAddressLoopback=true
  ```

  `hostAddressLoopback` lets the Windows PC itself open the host at its own network address,
  which matters when the host PC also joins with the app. Mirrored mode also needs the Hyper-V
  firewall to let the ports in; the script adds those rules.

- **Port proxy, works anywhere.** In an admin PowerShell on Windows, with the WSL address from
  `wsl hostname -I`:

  ```powershell
  netsh interface portproxy add v4tov4 listenport=8080 listenaddress=0.0.0.0 connectport=8080 connectaddress=<WSL address>
  netsh interface portproxy add v4tov4 listenport=8999 listenaddress=0.0.0.0 connectport=8999 connectaddress=<WSL address>
  ```

  Every viewer then looks like the same Windows address to the host, so viewers can't share
  pieces with each other. Downloads still work from your copy. The WSL address can change after a
  restart.

Either way, Windows Firewall has to let 8080 and 8999 in. For viewers outside your home, the
router also forwards them to the Windows PC. The setup check's **Windows to WSL** line says which
way the installer set up, and it warns when viewers all show up as one inside address.

For a test at home, the address viewers use is the Windows PC's own network address (`ipconfig`
in Windows, the IPv4 address of the Wi-Fi or Ethernet adapter), never the 172.x address inside
WSL. If the Windows network is set to **Public**, Windows may still block other PCs: set it to
**Private** in Windows Settings, Network.

## 3. Phones

**Synkplay** (Android, iOS) can join the same server and room. Open the night link on the phone:
its "On a phone" part has the downloads and the server, port, room and password, each with a
Copy button. Use Synkplay's mpv engine for proper anime subtitles. Phone support is experimental.

## Troubleshooting

| Problem | Try |
|---|---|
| Viewers can't connect to Syncplay | Port 8999 forwarded? Run the smoke test above from outside your network. |
| Hosting in WSL and other PCs can't reach it | Run the installer again and say yes to the Windows step (or run `windows-network.ps1` yourself). With mirrored networking, run `wsl --shutdown` once afterwards. Check the Windows network is **Private**, and that the address is the Windows PC's, not WSL's 172.x one. |
| WSL host worked, then stopped after a restart | With port forwarding, WSL's address changes after some restarts. Run the installer again. |
| The host page is down after Windows restarts | Open your WSL terminal once; Docker starts with it if systemd is on. Otherwise `sudo service docker start`. |
| Docker says permission denied | Your user isn't in the `docker` group yet. The installer uses sudo for now; to fix it for good, `sudo usermod -aG docker $USER`, then close and reopen the terminal. |
| Downloads only come from the host | Normal when only one person has the file. Group torrents need two people downloading at once, or a viewer who finished. |
| The setup check says viewers show up as an inside address | Docker or WSL is hiding their real addresses, so they can't share pieces. See "Running in WSL on Windows", or on a Linux server run the host with `network_mode: host`. |
| The app says it can't reach the room server | Port 8999 isn't reachable: Syncplay isn't running, or the port isn't forwarded. The host's setup check shows which. |
| Cleanup doesn't delete anything | The shelf folder must be writable by user 1654 (`sudo chown 1654 shelf`). |
| "Who's ready" shows nobody in the room | `TLP_ROOM_HELPER` must be on, and Syncplay must not run with `--isolate-rooms`. |
| Anything in the viewer app | **Settings**, **Copy details for a bug report**, and paste it into a [bug report](https://github.com/SergeantCS2/the-last-piece/issues/new?template=bug_report.yml). **Open log folder** shows the app's log. |
