# Product

<!-- impeccable:product-schema 1 -->

## Platform

desktop-windows (WPF, .NET 8). Not web, iOS or Android; the schema has no desktop value, so this is recorded literally.

## Stack

WPF / XAML on .NET 8, no third-party UI libraries so far. The stack is fixed.

## Users

A gamer tuning their own PC. Runs the app elevated (manifest requires administrator), mostly before or between gaming sessions, to apply performance tweaks per game and to customize the Windows desktop.

## Product Purpose

One place to (1) apply Windows and GPU performance tweaks per game, and (2) customize the desktop: taskbar behavior and look, wallpapers and slideshow, desktop widgets (clock, weather), and window snapping/tiling. Success: the user changes a setting and sees the effect immediately, with confidence that nothing is broken or irreversible.

## Positioning

Combines a gaming-optimizer workflow (per-game profiles and tweaks) with Seelen UI-style desktop customization in one app.

## Operating Context

Windows 11. Elevated process. Many settings write the registry or run PowerShell and apply automatically on change (debounced). Desktop widgets and snapping hotkeys exist only while the app is running. Weather data comes from Open-Meteo (no API key).

## Capabilities and Constraints

- Pages: Optimizer (mode, game list, Windows tweaks, display, latency, power, FPS), Task Manager (Processes with live monitors, Start-up apps, Free up resources with a Never kill list), Display, Hardware, Dock, Taskbar, Window Manager, App Settings.
- Registry-based taskbar settings; some keys are protected on newer Windows builds and may fail.
- Free up resources lists what can be stopped by scanning the PC; nothing is ticked by default, and security software, drivers, audio, anti-cheat and the programs on the Never kill list are never offered.
- Do not imply a setting is applied in UI copy when it is only saved.

## Brand Commitments

The name is CyberOptix. The look is dark with a violet accent, rounded glassy cards and toggle switches, in the spirit of modern desktop-customization tools. Treat any inspiration as a reference, not something to copy.

## Product Principles

- Immediate, visible effect: every control shows its current real state and what changed.
- Honest about what is and is not applied; never imply a setting took effect when it only saved.
- Safe to experiment: restore point on tweaks, easy reset, clear error messages.
- Dense but scannable: a power-user tool used repeatedly, not a one-time wizard.
