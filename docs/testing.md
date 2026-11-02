# First test: two PCs at home

A step-by-step run-through for the first real test. **PC 1** hosts in WSL and also joins with
the Windows app. **PC 2** joins with the Windows app to check that playback stays in step.
A third PC is optional. Every step says what you should see; if you don't see it, note the step
number and look at [If something goes wrong](#if-something-goes-wrong).

## What you need

- **PC 1**: Windows 10 or 11 with WSL 2 and a Linux distro such as Ubuntu. No WSL yet? In
  PowerShell as administrator run `wsl --install`, restart, and open Ubuntu once to make a user.
- **PC 2**: Windows 10 or 11.
- Both PCs on the same home network. Guest Wi-Fi often stops devices from seeing each other,
  so use the main network.
- One or two short video files, ideally MKV episodes with subtitles. Two lets you test moving
  to the next episode together.
- About 30 minutes.

## Part 1: the host, on PC 1

1. **Install.** Open Ubuntu (WSL) and run:

   ```bash
   curl -fsSL https://raw.githubusercontent.com/SergeantCS2/the-last-piece/main/install.sh | bash
   ```

   It checks for git, curl and Docker and offers to install what's missing (you'll be asked for
   your Linux password). Then it asks a few questions; the suggested answers are fine:
   - **Address other PCs use**: it suggests PC 1's Windows network address, like
     `http://192.168.1.20:8080`. Keep it.
   - **Folder with the episodes**: keep `./shelf`.
   - **Windows side**: pick **1, mirrored networking**, on Windows 11. Pick **2, port
     forwarding**, on Windows 10, or if mirrored gives you trouble later. A Windows admin prompt
     appears: choose **Yes**.

   *You should see:* "Ready", the host page address and an **admin password**. Keep that window
   open or copy the password.
2. **Mirrored only:** in Windows PowerShell run `wsl --shutdown`, then open Ubuntu again and wait
   about 30 seconds. The host starts by itself.
3. **Open the host page** on PC 1 at <http://localhost:8080> and sign in with the admin password.

   *You should see:* the **Setup check** at the top with no red lines. Yellow "works for PCs on
   your home network only" is expected for a test at home. **Windows to WSL** and **This browser
   reaches the public link** should be green.
4. **Add episodes.** Copy your video files into the folder the installer printed under "In
   Windows Explorer" (it looks like `\\wsl.localhost\Ubuntu\home\you\the-last-piece\server\shelf`).
   Reload the host page (F5).

   *You should see:* the files under **Shelf**.
5. **Make a session.** Under **New session**, tick the files, give it a title and a start
   time, and press **Create session and get link**.

   *You should see:* the session under **Sessions**, with **Copy link** and **Who's ready**.
6. **Check from PC 2's side early.** On PC 2, open `http://<PC 1's address>:8080/healthz` in a
   browser (the same address the installer used).

   *You should see:* `{"ok":true}`. If not, stop here and see "PC 2 can't reach the host" below.

## Part 2: join from PC 1

7. **Install the app.** Download `TheLastPiece-win-Setup.exe` from the
   [latest release](https://github.com/SergeantCS2/the-last-piece/releases) and run it. If
   Windows says it protected your PC, choose **More info**, then **Run anyway**.

   *You should see:* the app opens by itself with a **Welcome** box. (If it doesn't open, start it
   from the Start menu.)
8. **Welcome.** Name it `PC 1`, keep the folder, pick any look, paste the session link, and
   press **Get started**. If Windows asks whether the app may use the network, choose **Allow**.

   *You should see:* the session's title and its files.
9. **Download all.**

   *You should see:* each file finish with a check. On the host page, **Who's ready** on the session
   shows `PC 1` with its files done and the app open.

## Part 3: join from PC 2

10. **Send PC 2 the link** (Discord, email, anything) and repeat steps 7 to 9 there, naming it
    `PC 2`.

    *You should see:* downloads say "group torrent" or "the host's copy" and finish with a check.
    **Who's ready** now shows both PCs.

## Part 4: watch together

11. **Join** on both PCs.

    *You should see:* the player opens straight into the room, with no Syncplay window. On
    the host page, **Who's ready** shows both PCs in the room.
12. **Sync.** Press Space on PC 1: PC 2 should play within about a second. Pause on PC 2: PC 1
    pauses. Seek on either one: both jump to the same spot.
13. **Player.** Press **S** and **A** to pick subtitle and audio tracks, scroll for volume, and
    press **Enter** to send a chat message (it shows on both screens).
14. **Next episode** (with two files). Seek near the end of the first one.

    *You should see:* both PCs move to the second episode together.
15. **Rejoin.** Close the player on PC 2 and press **Join** again.

    *You should see:* PC 2 comes back to the same spot as PC 1.

## Part 5: optional extras

- **A third PC**: repeat Part 3 on it, and watch a three-way sync.
- **Looks**: in Settings, under Look, try each one. Motion stops when you click another window.
- **A phone**: open the session link on a phone and follow its "On a phone" part with Synkplay.
- **Discord post**: set `TLP_DISCORD_WEBHOOK` in `server/.env`, run the installer again, and
  press **Post to Discord** on the session.

## Part 6: uninstall

16. **App, on each PC**: Windows Settings, Apps, **The Last Piece**, Uninstall.

    *You should see:* it's gone from the Start menu. Your video files stay in their folder.
17. **Host, on PC 1**: in Ubuntu run `bash ~/the-last-piece/uninstall.sh` and answer its
    questions. It offers to undo the Windows network settings (one admin prompt).

    *You should see:* "Done", and the host page no longer opens.

## Sending back what you found

For anything that went wrong, [open a bug report](https://github.com/SergeantCS2/the-last-piece/issues/new?template=bug_report.yml)
with:

- the step number, and a screenshot;
- from each PC's app: **Settings**, **Copy details for a bug report**, then paste (it has the
  version and the app's recent log, and no passwords);
- from the host, if it's a host problem: in Ubuntu,
  `cd ~/the-last-piece/server && docker compose logs --tail 300 > host-log.txt`, and attach
  `host-log.txt` (Explorer: `\\wsl.localhost\Ubuntu\home\you\the-last-piece\server`).

## If something goes wrong

| Problem | Try |
|---|---|
| **PC 2 can't reach the host** (step 6) | On PC 1, re-run the installer and say yes to the Windows step; with mirrored networking, also run `wsl --shutdown` afterwards. Check both PCs are on the same network, not guest Wi-Fi. If mirrored still won't let PC 2 in, re-run and pick **2, port forwarding**. |
| It worked, then stopped after a restart | With port forwarding, WSL's address can change after Windows restarts. Re-run the installer and say yes to the Windows step. |
| The host page doesn't open on PC 1 after a restart | Open Ubuntu once: the host starts with WSL. If it still doesn't, run `sudo service docker start`. |
| The setup check has a red **Public link** | The address is WSL's own or `localhost`. Re-run the installer and accept the suggested address. |
| PC 1's own app can't open the link, but PC 2's can | With mirrored networking, run `wsl --shutdown` once more (the installer added the setting this needs). Or re-run the installer and pick port forwarding. |
| **Join** says it can't reach the room server | Port 8999 isn't getting through: same fixes as "PC 2 can't reach the host". |
| Downloads only come from the host's copy | Expected with port forwarding, and when only one PC is downloading. |
| A download fails | Press **Download all** again; finished parts are kept. Then send the details. |
| Windows asked about the network and you chose Cancel | Downloads still work. To share pieces, allow The Last Piece in Windows Security, Firewall, "Allow an app through firewall". |
