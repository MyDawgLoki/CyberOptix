# Changelog

Every release lists what is new, what was fixed and what changed. The same text is in the release notes on GitHub.

## Unreleased

**Changed**
- The **Ultimate Performance power plan is off by default** (Gaming and General profiles). That plan keeps the CPU at full speed even when idle, which made the PC run hot and the fans loud as soon as a game started. It is still there as an option; it applies only if you turn it on and save it in your Gaming profile. Profiles saved by earlier versions are switched to the new default once.
- The Apps panel can now **move your desktop into it automatically** each time CyberOptix starts (shortcuts, apps, files and folders), so you no longer have to open the panel and press the button after a restart or after the watchdog put everything back. There is an **Auto-move at start-up** switch in the panel. It starts on only if you had used the panel before; Move To Desktop still puts everything back.

## v1.0.4

**New**
- A **crash handler**: an error nobody handled is written to `crash.log` (error and call stack, personal folders replaced by placeholders, kept under 30 KB, never sent anywhere). One error on the screen's thread is logged and CyberOptix carries on; a burst of errors, or a fatal one, puts the Windows taskbar back and restarts CyberOptix once (at most once every five minutes, so a start-up bug cannot loop).
- A **Sleep** section on the Display page: when Windows turns the screens off and puts the PC to sleep, plugged in and (on a laptop) on battery, plus a "Turn the screens off now" button. They are Windows' own power-plan timers, the same ones as in Windows Settings.

**Changed**
- A live wallpaper from a **web address** now asks you to approve that site first (with a warning that the page runs inside CyberOptix, which has administrator rights). It only runs for approved sites, the page can only stay on that site, and **Forget approved web addresses** clears the list. Web pages from a file are not affected.
- Applying an Optimizer profile starts one PowerShell for the restore point, the SysMain service and the network tweak, instead of one for each setting. Registry values and the power plan are written directly, so there are far fewer short CPU spikes.
- A game or fullscreen app no longer hides the dock. With "Fall behind a game or fullscreen app" on (it was "Hide while a game or fullscreen app is open"), every dock drops behind the game, and the Windows key brings it forward with the Start menu. It goes back behind the game when the menu closes.

**Fixed**
- Settings can no longer be lost to a power cut or a crash while they are being saved. They are written to a temporary file and swapped in, the previous copy is kept as `settings.json.bak`, and if the main file is ever unreadable CyberOptix loads the backup (and keeps the bad file as `.corrupt`) instead of silently starting from defaults. Saves from two places at once now take turns.
- A live wallpaper from a web address can only stay on its own approved site. Before, it could also follow a link to any other site you had approved.
- The crash log is written by one error at a time, so two errors at once cannot garble it.
- Applying the Optimizer profile no longer adds another copy of the Ultimate Performance power plan every time: it is made once and reused, and if Windows will not make it, High performance is used. A **Remove duplicate power plans** button appears under the Ultimate Performance option while Windows holds extra copies; it keeps one and touches no other plan.
- With the dock reserving its own strip of the screen (Stay On Top, "Let windows reach the top" off), CyberOptix no longer also resets the screen's work area to full height every 2 seconds, which fought the dock.
- The check that spots a game or browser in front now reads the program's path through a limited-rights handle instead of opening the whole process, which is gentler on games with anti-cheat.
- Force-quitting CyberOptix (Task Manager, End process tree, a crash) now brings the Windows taskbar back and moves every desktop item in the Apps panel back onto the desktop. A small watchdog (another copy of CyberOptix.exe, started with `--watchdog`) waits for the real one; closing CyberOptix normally, restarting it, or shutting Windows down leaves things as they are.
- Replacing the taskbar no longer asks Windows to turn on its "Automatically hide the taskbar" setting. That request made Explorer show "A toolbar is already hidden on this side of your screen" when a screen came or went (a virtual or remote display, for example), and left the setting switched on. The taskbar is hidden and its strip handed back instead.
- Two copies of CyberOptix could run at once (for example after a restart that began with the startup task, then opening it again), and they fought over the taskbar: it kept hiding and coming back, and Windows showed "A toolbar is already hidden on this side of your screen". The "one at a time" check now looks twice (the named lock, and any other CyberOptix in the same sign-in session) and gives a closing copy a few seconds to let go before a new one gives up. A second launch just brings the running one to the front.

## v1.0.3

**Fixed**
- The main dock could vanish and not come back when set to "Fall Behind Windows" or "Stick Behind Windows". Any window that filled the screen without being maximized (a borderless window, a browser in full screen) hid the dock for good, and a hidden dock cannot be brought forward by the pointer or the Start menu. A dock that sits behind the windows is no longer hidden. The same fix applies to the individual docks.

**Changed**
- The Dock page: the main dock's settings are now one row under a **Main Dock** title (position, Dock and windows, Show the dock). Each individual dock header is one row too (position, Dock and windows, Individual Dock switch).
- A game that covers the screen no longer hides a dock set to Fall or Stick; the dock stays behind the game, and the pointer at its screen edge can still bring it forward.

## v1.0.2

**New**
- A color wheel (brightness slider, typed hex box, Reset) for the accent, window glow, dock glow and desktop clock colors.
- A **Windows colors** card: tint Start, Search, the taskbar, title bars and File Explorer / Settings with the CyberOptix color, a color you pick, or the media player's color; light, dark and transparency choices. Your own Windows colors are saved first and put back when you switch it off.
- The desktop clock can sync its color with the media player, with any clock theme.
- Every icon dock has its own **Dock and windows** setting (stay on top, fall behind windows with hover, or stick behind), defaulting to fall behind.
- Task Manager: a Never kill list tab, with the Free up resources buttons at the top. Media player buttons show each service's logo.

**Fixed**
- The Start menu and Search sit flush with the bottom of the screen when the dock replaces the taskbar.
- Maximized windows reach the bottom edge while the taskbar is hidden (no leftover gap).
- The animated wallpaper recovers from a failed video player instead of falling back to the still picture.

**Security**
- Restoring your Windows colors only touches the eleven values CyberOptix changes. SECURITY.md documents the new behaviors.
