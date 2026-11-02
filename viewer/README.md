# Joining a session

The short version is in the main [README](../README.md#joining-a-session); this page adds the
details.

## Windows (The Last Piece app)

1. Download `TheLastPiece-win-Setup.exe` from the [latest release](https://github.com/SergeantCS2/the-last-piece/releases) and run it. It installs for you only, with no admin prompt.
   - The app isn't code-signed yet, so Windows may say it protected your PC. Click "More info", then "Run anyway".
   - Prefer no install? Use the portable zip from the same release: unzip it anywhere and run `TheLastPiece.exe`.
2. The first time it opens, a Welcome box asks for your name, your video folder and a look, and takes the session link if you have one. If Windows asks whether the app may use the network, choose Allow, so you can share pieces with the others.
3. Click the session link your host sends, then "Open in The Last Piece". Or paste the link into the app and press Open.
4. Press "Download all". Each file comes by torrent or from the host's copy, and the app checks it matches.
5. At showtime press "Join session". The player opens in the room.

Nothing else to install: mpv and Syncplay come with the app. Updates download in the background
and install the next time the app opens.

## Already use VLC or your own Syncplay?

That still works. Copy the file names from the session page, put the files in your Syncplay folder, and connect to the server and room on the page. On-screen chat only shows in mpv.

## Android / iOS

**Synkplay** from the Play Store or App Store. Use the mpv engine for proper anime subtitles. Phone support is experimental.

## During a session

- Pause, seek, play: everyone follows.
- Your volume, window size, subtitles and audio track are yours alone.
- Subtitles: press **S** for a menu of subtitle tracks, **A** for audio tracks, or right-click for the full menu. **V** and **B** step through them like VLC.
- Volume: mouse wheel, arrow keys or Ctrl+Up/Down, up to 300% (set the limit in the app's Settings). Very high volume can distort loud scenes.
- Press **Enter** in the player to type a chat message; it shows on everyone's screen.

## Something went wrong?

In the app, open Settings and press **Copy details for a bug report**. It copies the app's
version, your Windows version and the app's recent log, with no passwords or links. Paste it into
a [bug report](https://github.com/SergeantCS2/the-last-piece/issues/new?template=bug_report.yml).
**Open log folder** shows the log itself.
