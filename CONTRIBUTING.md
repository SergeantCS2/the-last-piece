# Contributing

Thanks for helping. This is a small project for watching things together; changes that keep it
simple for non-technical friends are the most welcome.

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

Maintainers push a tag like `v0.4.0`. CI builds, signs when signing is set up, tests the
installer and publishes a GitHub Release, which installed apps update from.
