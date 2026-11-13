# Changelog

Every release lists what is new, what was fixed and what changed. The same text is in the release notes on GitHub.

## Unreleased

**Fixed**
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
