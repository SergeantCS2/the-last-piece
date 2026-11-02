# Contributing

Thanks for helping. This is a small project for watching things together; changes that keep it
simple for people who aren't technical are the most welcome.

## Build and test

Needs the .NET 10 SDK. Docker is only needed for the host image.

```bash
dotnet test TheLastPiece.sln
```

The tests include a real two-peer torrent through the host app and a fake Syncplay server, so
they need no network. The viewer app builds on Linux too; its self-test renders the window
off-screen:

```bash
dotnet build -c Release src/Tlp.Viewer
xvfb-run -a dotnet src/Tlp.Viewer/bin/Release/net10.0/TheLastPiece.dll --self-test out/
```

## Layout

| Path | What |
|---|---|
| `src/Tlp.Core` | Shared: movie night pack format, magnets, app name |
| `src/Tlp.Host` | Host app: API, admin page (`wwwroot`), Syncplay room helper, group torrents |
| `src/Tlp.Viewer.Core` | Viewer logic: downloads, torrents, file checks, Syncplay launch |
| `src/Tlp.Viewer` | Windows app (Avalonia), themes, installer hooks |
| `viewer/` | Bundled mpv settings and keys |
| `server/` | Docker Compose, Syncplay image, Caddy |
| `tools/` | Packaging, installer, Syncplay smoke test |

## CI

Every push runs:
- `dotnet`: tests on Linux; on Windows it builds the zip and installer, runs the real app's
  self-test, joins a real Syncplay server, checks mpv loads our settings, and installs and
  uninstalls `Setup.exe`.
- `server`: builds the images, starts them with Compose, and checks a night's playlist reaches
  Syncplay.

## Conventions

- The app's name lives only in `Directory.Build.props`.
- Colours, fonts and sizes live only in theme files (`src/Tlp.Viewer/Themes`). See the README
  there to add a theme. Use your own colours and freely licensed fonts; no artwork, logos or
  names from games or films.
- Say how you checked a change in the pull request. "Tested on Windows with two people" and
  "only unit tests" are both fine; just be clear which.
- The project stays content-neutral: no built-in torrent search or index.

## Releases

Maintainers push a tag like `v0.4.0` (or make the release on GitHub's release page). CI builds,
signs when signing is set up, tests the installer and publishes a GitHub Release, which
installed apps update from.

### Installer and updates

CI builds a Velopack installer (MIT) next to the zip, in the `TheLastPiece-installer` artifact:
- `TheLastPiece-win-Setup.exe` installs to `%LOCALAPPDATA%\TheLastPiece` with no admin prompt, adds a Start menu entry, and registers `tlp://` links. Uninstalling from Windows Settings removes the link registration too.
- The installed app checks GitHub Releases in the background. A new version downloads quietly and installs the next time the app opens, or straight away with "Restart to update". It never restarts while a download is running.
- Releases are made by a tag like `v0.3.0`, pushed with git or made on GitHub's "Draft a new release" page (CI then adds its files to that release). CI then builds, signs if set up, tests the installer and publishes the release, marked pre-release for now; the app follows pre-releases. Nothing is published from ordinary pushes.
- The zip stays for anyone who'd rather not install.

`Setup.exe` is about 104 MB, a little more than the 88 MB zip because Velopack compresses less tightly.

PROVEN on a Windows CI machine: silent install, `tlp://` registered to the installed app, uninstall removing both. INFERRED until two releases exist: a real update from one version to the next.

### Signing

Without a signature Windows shows "Windows protected your PC" on first run. Every option below needs the publisher's own account and identity, and most cost money, so none is set up yet. The CI already has a slot for Azure Artifact Signing: add three repository variables (`SIGN_ENDPOINT`, `SIGN_ACCOUNT`, `SIGN_PROFILE`) and three secrets (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`) and the next build is signed. mpv and Syncplay are left exactly as their projects ship them.

| Route | Cost | Who can use it | Warning goes away? |
|---|---|---|---|
| Microsoft Store | Free for individual developers | Anyone with a Microsoft account and ID check | Yes, straight away. Microsoft signs it. Needs extra work: an MSIX package, and Syncplay settings moved out of the read-only install folder. |
| Azure Artifact Signing | About $10 a month | Individuals in the US and Canada, companies in more places; Azure account and ID check | Not at first. It fades as downloads build reputation. |
| SignPath Foundation | Free | Open-source projects with an OSI licence, a published release, and a short signing policy page | Same as Azure. The certificate is in SignPath's name. |
| OV certificate | About $150 to $300 a year | Anyone, with ID check | Same as Azure. |
| EV certificate | $400 or more a year | Companies mostly | Same as Azure since 2024; it no longer skips the warning. |

Costs and rules are from the providers' pages in late 2026 (INFERRED that they hold at sign-up time).

More detail on the design is in [docs/how-it-works.md](docs/how-it-works.md); the manual test
run-through is [docs/testing.md](docs/testing.md).
