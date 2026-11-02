# Manual test checklist

A run-through for a host and one viewer before a real night, or before a release. Each step says
what to look for.

1. **Host.** Start the host (Docker or native) and open the admin page. The setup check at the top should have no red lines; yellow ones say what to look at. Create a night with one episode from the shelf. The night should show a "Group torrent" button after a minute (the torrent file is made in the background).
2. **Install.** Run `TheLastPiece-win-Setup.exe` from the release (or the `TheLastPiece-installer` artifact of a green `dotnet` run, to test a build before release). Expect a SmartScreen warning until signing is set up ("More info", "Run anyway"). The app should open by itself.
3. **Link.** Click the night link in a browser and choose "Open in The Last Piece". The app should open on that night. Click a second link while it's open: the same window should switch to it, with no second window.
4. **Download.** Press "Download all". It should say "group torrent" and finish with a green check. With a second viewer, start the second one after the first finishes and see that it's quick.
5. **Who's ready.** On the admin page, open "Who's ready" on the night. Each viewer should show their files done and "app open".
6. **Join.** Press "Join movie night". mpv should open straight into the room, with no Syncplay window. Pause on one PC: the other should pause. "Who's ready" should now show both in the room.
7. **Next episode.** With a two-episode night, let the first end (or seek near the end). Both should move to episode 2 together.
8. **Player.** Check subtitles look right on an anime MKV, and press Enter in mpv to send a chat message.
9. **App looks.** In Settings, Look, try each one. Motion should be smooth and stop when you click another window.
10. **Phone (optional).** Open the night link on a phone and follow "On a phone" with Synkplay.
11. **Discord (optional).** Set `TLP_DISCORD_WEBHOOK` and press "Post to Discord" on the night. Check the time shows in your own time zone.

If something goes wrong, open an issue with a screenshot and the step number. The host's setup check and the app's status line say in plain words what failed (a closed port, an old link, a server that isn't running).
