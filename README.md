<div align="center">

# Velocita and Tecnica

**Two separate Windows apps for the sim rig.**

**Velocita** is for racing in iRacing: overlays, stats and setups. **Tecnica** sets up the PC: displays, fans, windows and a performance overlay.

### [Download the latest release](https://github.com/king-tulip999/Distro/releases)

</div>

I got sick of setting everything up by hand every time I went from my desk to my sim rig, and of paying for a handful of subscriptions just to race. So I spent the last couple of years building it. Velocita is the toolkit I use every time I race in iRacing; Tecnica sits in the tray and sets the PC up for whatever I'm doing. Now you can use them too.

## Two apps, use one or both

They're two separate programs: two exes, two tray icons, their own settings and their own updates. Neither needs the other, so take only the one you want.

| | Velocita | Tecnica |
| --- | --- | --- |
| **For** | Racing in iRacing | Setting up the PC |
| **Download** | `Velocita.exe` | `Tecnica.exe` |
| **Administrator rights** | Not needed | Needed (sensors, fans, displays) |
| **Settings folder** | `%APPDATA%\Velocita` | `%APPDATA%\Tecnica` |
| **Tools** | iRacing Overlay, Session Stats, Setup Manager, iRacing Controller, FOV Calculator, bass shakers, companion apps, GRR League | Displays, Window Profiles, Performance Overlay, Fan Control |

---

## Velocita: for racing in iRacing

Everything you use around an iRacing session, in one app that runs as a normal user.

| Tool | What it does |
| --- | --- |
| **iRacing Overlay** | Relative, standings, fuel and strategy, pit exit, radar, inputs, track map and more, edited in the car. |
| **Session Stats** | Your whole iRacing history on your PC: result cards, goals, lap analysis and replays. |
| **iRacing tools** | Setup Manager, iRacing's settings and keybinds in plain language, FOV calculator, bass shakers, Trading Paints and Garage 61. |
| **GRR League** | Your league's next race, standings, results and schedule (only if you turn the page on). |

<details>
<summary><b>iRacing Overlay</b>: 19 widgets, in-game editor, browser sources</summary>

<br>

- **Widgets:** Relative, Standings, Fuel and strategy, Lap timing and delta, Lap history, Tires, Weather and track, Wind, Session, Race events, Multiclass traffic, Alerts, Car radar, Track map, Inputs, Practice goals, Notes, a Race result after the race and a Data block for any single value.
- **Inputs:** throttle, brake, clutch and steering with gear, speed and shift lights, as a scrolling trace or a low shift-light bar.
- **Pit exit:** the Fuel widget shows where a stop right now would put you: your class position, the cars ahead and behind, traffic, and a ghost on a track strip. It learns the pit lane, fill rate and tire time per track and car.
- **Strategy:** fuel saving (what each lap may use to finish with one stop fewer) and undercut / overcut (what you'd need per lap to jump the car ahead, or to cover the one behind).
- **Practice goals:** clean or consistent laps in a row, laps close to your best or under a time, as a lap list or big counters.
- **Notes:** your notes per track and car, with the next corner's note lighting up as you get close.
- **Race result:** a card, a banner, a stamp or a lap chart once you take the flag.
- **Track limits:** off-tracks counted and placed by corner, with an alert from a threshold you pick.
- **Your layout:** every widget can be set up however you like, with layouts that switch by car / class / track. Colors: Default, your own, or any app theme's (teams, custom color sets) from one menu.
- **In-game editor:** `Ctrl+Alt+E`, so you never have to leave the car.
- **Browser sources:** every widget is also a web page on your PC only (`127.0.0.1`), for a wheel screen, a tablet browser or OBS. Off by default (iRacing Overlay → Settings → Browser sources).

</details>

<details>
<summary><b>Session Stats</b>: history, result cards, goals, lap analysis</summary>

<br>

Your iRacing history, kept on your PC.

- **History:** laps, time, results, ratings, incidents and personal bests by week / month / season, with a picture of each car. Panels can be dragged into your own order.
- **Result cards:** every race gets one with your finish, positions gained, rating changes, pace, consistency and awards (Win, Podium, Clean race, Personal best, Charger and more).
- **Goals:** set up to 6 goals for iRating or safety rating (per license), races, wins, podiums, clean races or hours (per car / track or this season). Reached goals are kept with their date and show on that race's result card.
- **Per car and track:** a report card, where your off-tracks happen on the map, and your notes.
- **Lap analysis:** open any session for its laps and sectors, then compare laps on telemetry traces (including brake bias, ABS and traction control) or a top-down replay, and see which corners cost the most time. Export a lap to share and import other people's.
- **Tire wear:** tracked per run from the pit-stall readings.
- **Old telemetry:** reads your old iRacing telemetry files so your history doesn't start from zero, can switch on iRacing's telemetry recording, and can clear out files it has already read.

</details>

<details>
<summary><b>iRacing tools</b>: setups, settings and keybinds, FOV, bass shakers, companion apps</summary>

<br>

**Setup Manager**

- **Sorted:** all your setups by car and class.
- **Organize:** rename, move, import share packs and pick up new downloads.
- **Fuel planning:** lives in the iRacing Overlay's **Fuel** tab.

**iRacing Controller**

- **Settings:** iRacing's settings in plain language. Search, see what differs from iRacing's defaults and reset it.
- **Keybinds:** your in-sim keybinds per control profile, grouped the way iRacing groups them, with keys drawn as keycaps and wheel buttons marked.
- **Find a key:** shows what a key combo does; filters show what's set, not set, changed or on the wheel.
- **Conflicts:** setting a key that's already used offers to swap them or keep both.
- **Undo:** any change can be undone from its row, and everything gets backed up before it's changed.

**FOV Calculator**

- **Rigs:** single and triple screens, compare rigs, and write the result straight into iRacing.
- **Checks:** tells you when iRacing's FOV doesn't match your rig, or your side screens aren't at the best angle.

**Bass shakers**

- **Effects:** engine, gear shifts, ABS, curbs and bumps, and the rev limiter from iRacing's telemetry, played through any sound output your shaker's amplifier is on.
- **Other games:** can use the low rumble from the game's own sound instead (iRacing too, if you prefer).
- **Tuning:** each effect has its own strength and a test button.

**Companion apps**

- **Start and stop:** Trading Paints and Garage 61 with Velocita or when iRacing starts.
- **Your paint:** car pictures across the app can show it.

</details>

<details>
<summary><b>GRR League</b>: your league in the app</summary>

<br>

If you race in the GRR league, turn on the GRR League page (on the setup screen, or Settings → Application → GRR League page). It shows your next race and the season's schedule, the championship and results for your class, and where you stand, all from the GRR website without leaving the app. It's off by default, so nobody else sees it.

</details>

---

## Tecnica: for setting up the PC

Switches the whole PC between setups (desk, sim rig, streaming) and keeps an eye on it. It runs as administrator because sensors, fans and display changes need it.

| Tool | What it does |
| --- | --- |
| **Displays** | Monitors, resolution, HDR and audio saved as profiles you switch with a keybind. |
| **Window Profiles** | Any program at an exact size and spot, borderless, applied when it starts. |
| **Performance Overlay** | FPS, 1% lows, temperatures and usage over your game. |
| **Fan Control** | Curves, per-fan limits and a failsafe that hands fans back to the BIOS. |

<details>
<summary><b>Displays</b>: monitor profiles you switch with a keybind</summary>

<br>

- **Monitors:** turn them on and off, arrange them, duplicate one onto another and give them your own names.
- **Settings:** resolution, refresh rate, HDR, taskbar, wallpapers and audio.
- **Profiles:** save it all (mine are *Desk* and *Sim rig*) and switch between them with a keybind.
- **Safe changes:** layout changes ask you to keep them and go back by themselves after 15 seconds if you don't. If Tecnica closes before you choose, the next start puts the displays back.

</details>

<details>
<summary><b>Window Profiles</b>: exact size and spot for any program</summary>

<br>

Pick a program and give its window an exact size and spot: a game stretched across triple screens, held at a fixed resolution, or moved onto a side screen, with its borders removed.

- **Presets:** your own displays, triple 1080p / 1440p / 4K and common window sizes.
- **Applying:** when the program starts (with a delay for games that resize themselves), with a keybind, from the tray or with one click.
- **Groups:** move several programs at once.
- **Preview:** each profile shows a small picture of where its window goes on your displays.

</details>

<details>
<summary><b>Performance Overlay</b>: FPS and sensors over your game</summary>

<br>

- **Readings:** FPS, 1% lows, average FPS and temperatures, usage and more drawn over your game.
- **Layouts:** drag readings into whatever order you want, keep several layouts, and reset any of them to the default.

</details>

<details>
<summary><b>Fan Control</b>: curves, limits and a failsafe</summary>

<br>

- **Curves:** presets, calibration and an emergency failsafe.
- **Limits:** each fan has its own minimum, fan stop, and how fast it may speed up or slow down.
- **Failsafe:** if the hardware stops answering, the fans go back to the BIOS / driver until it's back.
- **On quit:** keep your curves running in a small background helper (its own tray icon), or hand the fans back to the BIOS / driver.

</details>

---

## Getting started

1. Download `Velocita.exe`, `Tecnica.exe` or both, and put them somewhere they can stay (e.g. `C:\Tools`).
2. Run one and answer Windows' prompts (see [Before you run it](#before-you-run-it)).
3. On the setup screen, pick your startup options (and in Velocita, a theme and whether to show the GRR League page), then click **Get started**. Everything can be changed later in Settings.
4. *Optional, Tecnica:* install the free **PawnIO** driver for CPU temperatures and motherboard fans (Settings → Hardware access → **Install PawnIO**).

> [!IMPORTANT]
> Windows warns about SmartScreen (**More info → Run anyway**) for both. Tecnica also asks for administrator rights (**Yes**); Velocita doesn't.

> [!TIP]
> With **Start with Windows** on and Tecnica in a folder other programs can write to, Settings offers **Move to Program Files** so the startup entry can't be used to get administrator rights.

> [!NOTE]
> Coming from Pit-Box? The first start of each app copies what it needs from the old settings folder: Tecnica its displays, fans and profiles; Velocita its overlay layouts, stats, lap traces and iRacing backups. The old folder is left as it was. If Pit-Box still runs or starts with Windows, each app tells you, and Tecnica can remove Pit-Box's startup entry.

## Before you run it

<details>
<summary><b>"Windows protected your PC"?</b></summary>

The exes aren't code-signed (certificates cost money every year). Click **More info → Run anyway**. If you want to check your copy, compare `Get-FileHash Velocita.exe` (PowerShell) with `Velocita.exe.sha256` on the release page (the same for Tecnica). The in-app updater also checks each download against the release signature (`.sig`) before installing it.
</details>

<details>
<summary><b>Why does Tecnica need administrator?</b></summary>

Frame counting, sensors, fan control, display changes and detecting games all need it. With **Start with Windows** on, it starts when you sign in without a prompt. Velocita runs without it.
</details>

<details>
<summary><b>What is PawnIO?</b></summary>

A small, free, open-source, signed driver Tecnica can use for CPU sensors and motherboard fans (it replaces WinRing0, which Defender flags). It's optional: without it CPU temperature shows *Not available* and motherboard fans can't be controlled. Everything else works. Velocita never uses it.
</details>

<details>
<summary><b>Safe with anti-cheat?</b></summary>

Nothing gets injected into games. The overlays are separate click-through windows. If a strict anti-cheat ever complains about the driver, uninstall PawnIO.
</details>

<details>
<summary><b>Antivirus flagged it?</b></summary>

Unsigned admin apps that read hardware sometimes trip heuristics. If the checksum matches the release, it's the build I published.
</details>

<details>
<summary><b>What does it send online?</b></summary>

Tecnica: a small ping for the network readout (only while it's on screen). Velocita: the GRR League feed, only if you show that page. Both: an update check against GitHub releases if you leave that on. Browser sources only answer on your own PC. No accounts, no telemetry. Your stats stay on your PC.
</details>

<details>
<summary><b>What does it change?</b></summary>

Only what you set up. Velocita backs up iRacing files before changing them. Tecnica hands the fans back to the BIOS / driver when it quits.
</details>

## Limitations

- Velocita's tools only support iRacing (bass shakers can also use other games' sound).
- Overlays show over borderless and windowed games, not exclusive fullscreen.
- Windows text scaling (100%, 125%…) has no supported API, so Tecnica's Displays page just links to Windows settings for it.

> [!WARNING]
> If Tecnica gets force-killed (Task Manager → End task), fans stay at their last speed until it restarts or the PC reboots.

## Updates and removal

- **Updates:** each app's Settings → Application → About and updates checks on start, daily, weekly or only when you click **Check now**. When there's an update, **Install and restart** downloads it, checks its checksum and signature and swaps the exe. Your settings are kept. Each app updates only itself.
- **Storage (Velocita):** Settings → Data and configuration → Storage shows how much space replays, paints, telemetry and Velocita itself use, and moves old replays and other drivers' paints to the Recycle Bin.
- **Reset:** Settings → Data and configuration → **Reset Velocita** / **Reset Tecnica**. Your old settings are saved as `config.before-reset-<date>.json`, and **Export / Import** lets you back them up or move them to another PC.

<details>
<summary><b>Uninstall</b></summary>

1. Turn off **Start with Windows** in each app.
2. Quit from the tray (in Tecnica, choose **Give fans back and quit** if fan control is on).
3. Delete `Velocita.exe` / `Tecnica.exe` and `%APPDATA%\Velocita` / `%APPDATA%\Tecnica`.
4. If you installed PawnIO for Tecnica, remove it from Windows Settings → Apps.
</details>

## Credits

<details>
<summary>Velocita and Tecnica are built on these, each under its own license.</summary>

<br>

| What | Used for | License |
| --- | --- | --- |
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | Sensors and fan control | MPL 2.0 |
| [.NET, WPF and Windows Forms](https://github.com/dotnet) (Microsoft) | The app framework | MIT |
| [Lucide](https://lucide.dev) | Icons (some adapted) | ISC |
| [Saira](https://github.com/Omnibus-Type/Saira) by Omnibus-Type | The app font, wordmark and the iRacing Overlay's numbers | SIL Open Font License 1.1 |
| [SIMRacingApps](https://github.com/SIMRacingApps/SIMRacingAppsServer) by Jeffrey Gilliam | Track centerline and pit lane routes for Session Stats' track maps | Apache 2.0 |
| Track map drawings by [iRacing](https://www.iracing.com), collected in [racing-track-maps-vector](https://github.com/meowmachine/racing-track-maps-vector) by meowmachine | Track maps for Session Stats and the iRacing Overlay | © iRacing.com Motorsport Simulations, LLC |
| [PawnIO](https://pawnio.eu) | Optional driver for CPU and motherboard sensors (not bundled; installed separately) | See its site |

**Inspired by** (no code or assets from these are used):

- [Fan Control](https://github.com/Rem0o/FanControl.Releases) by Rem0o: the Fan Control page
- [Resize Raccoon](https://github.com/mistenkt/resize-raccoon) by mistenkt and its fork [Resize Rabbit](https://github.com/rosscarlson/resize-rabbit) by rosscarlson: Window Profiles

Full license texts ship with the components (for example `NOTICES.md` inside the track map library and `Saira-OFL.txt` beside the fonts).

</details>

Thanks to the [GRR League](https://www.grassrootsracing.org) for letting Velocita use its site data and logo.

<sub>iRacing is a trademark of iRacing.com Motorsport Simulations, LLC. Trading Paints and Garage 61 belong to their owners. Velocita and Tecnica aren't affiliated with or endorsed by any of them.</sub>
