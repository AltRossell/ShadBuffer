<div align="center">

# ShadBuffer

**Open-source instant replay for Windows, powered by OBS Studio.**

A free alternative to NVIDIA ShadowPlay / Instant Replay that sets itself up in one click.

[![Latest release](https://img.shields.io/github/v/release/seandotnet/ShadBuffer?label=release&color=6d5ef5)](https://github.com/seandotnet/ShadBuffer/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/seandotnet/ShadBuffer/total?color=6d5ef5)](https://github.com/seandotnet/ShadBuffer/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-6d5ef5)](LICENSE)
![Windows 10 and 11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6)

![](https://dcbadge.limes.pink/api/shield/1474314300158902314) [![](https://dcbadge.limes.pink/api/server/QJcDHybHyu)](https://discord.gg/QJcDHybHyu)

<img width="1350" height="720" alt="ShadBuffer overlay with the clips library open" src="https://github.com/user-attachments/assets/99b93330-f64c-4eab-85e7-7d8076d31009" />

[Download](https://github.com/seandotnet/ShadBuffer/releases/latest) &nbsp;&bull;&nbsp; [Install](#install) &nbsp;&bull;&nbsp; [Features](#features) &nbsp;&bull;&nbsp; [Hotkeys](#hotkeys) &nbsp;&bull;&nbsp; [Discord](https://discord.gg/QJcDHybHyu)

</div>

## About

ShadowPlay kept turning itself off on me because of DRM errors, so I built a replacement on top of OBS Studio. ShadBuffer configures OBS to keep a rolling replay buffer in the background, so you can save the last few minutes of gameplay with a single hotkey. An overlay lets you record, take screenshots, browse your clips and share them without ever opening OBS.

## Features

- **Instant Replay** keeps the last 30 seconds to 10 minutes in memory. Press a hotkey to save it as a clip.
- **Recording** starts and stops full-length recordings from the overlay or a hotkey.
- **Screenshots** are saved next to your clips with one key.
- **Overlay** opens a sidebar with Instant Replay, recording, screenshot and microphone controls.
- **Clip library** sorts clips and recordings into a folder per game, with thumbnails, sorting by date or size, and a built-in player.
- **Sharing** lets you copy the video file, or upload it to kappa.lol and get a link you can paste anywhere.
- **Microphone control** with device selection, an on/off toggle that works without restarting OBS, and optional RNNoise or Speex noise suppression.
- **Game or desktop capture**, with desktop capture available for games with strict anti-cheat.
- **Hardware encoding** on NVIDIA, AMD and Intel, using H.264, HEVC or AV1 when your GPU supports it.
- **One-click setup and removal**, using its own OBS profile so your existing profiles are left alone.

## Install

1. Download `ShadBuffer.exe` from the [latest release](https://github.com/seandotnet/ShadBuffer/releases/latest).
2. Run it, click **Set up** and accept the admin prompt.

Or run this in PowerShell:

```powershell
irm https://raw.githubusercontent.com/seandotnet/ShadBuffer/main/install.ps1 | iex
```

> [!NOTE]
> The exe isn't code signed yet, so Windows SmartScreen may show a warning. Click **More info**, then **Run anyway**.

**Requirements:** Windows 10 or 11. OBS Studio 31 or newer is installed for you if it's missing or out of date.

## Hotkeys

| Action | Default |
| --- | --- |
| Save the last few minutes | <kbd>Alt</kbd> + <kbd>F10</kbd> |
| Open the overlay | <kbd>Alt</kbd> + <kbd>X</kbd> |
| Start or stop recording | <kbd>Alt</kbd> + <kbd>F9</kbd> |
| Save a screenshot | <kbd>Alt</kbd> + <kbd>F1</kbd> |
| Turn Instant Replay on or off | <kbd>Alt</kbd> + <kbd>Shift</kbd> + <kbd>F10</kbd> |
| Turn the microphone on or off | <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>M</kbd> |

All hotkeys can be changed in **Settings > Hotkeys**. While ShadBuffer is running these combinations belong to it, so a game that uses one of them won't receive it. Press <kbd>Delete</kbd> in a hotkey box to turn that hotkey off.

## Where your files go

| Type | Location |
| --- | --- |
| Clips | `Videos\Clips\<game>` |
| Recordings | `Videos\Recordings\<game>` |
| Screenshots | Next to your clips, in the game's folder |

Both folders can be changed in **Settings > Clips**, along with clip length and sorting by game.

## Sharing clips

**Upload to kappa.lol** sends the clip to [kappa.lol](https://kappa.lol), a free third-party file host, and gives you a public link. Uploads only happen when you click the button.

- Files can be up to 100 MB.
- Anyone with the link can watch the clip.
- kappa.lol stores your IP address and doesn't state how long it keeps files, so use **Delete** when you're done.

## What it changes on your PC

- Installs OBS Studio if you don't have it.
- Adds its own OBS profile and scene collection. Your other profiles stay as they are.
- Starts OBS in the tray at login, running as admin. Both can be turned off in settings.
- Turns off Xbox Game Bar capture and the OBS exit warning.

**Remove** in the app puts all of that back. Your clips and OBS itself are kept.

## Good to know

- **Anti-cheat:** game capture hooks into fullscreen games, so some anti-cheats may block it or object. Switch to desktop capture in settings for games like Valorant, CS2 or FACEIT, since it doesn't touch the game. The overlay itself is a normal window and never injects into games.
- **Desktop capture** records the whole monitor, so notifications and the overlay can end up in clips.
- **Microphone:** your mic is recorded by default. Turn it off from the overlay if you don't want that.
- **DRM:** ShadBuffer doesn't get around DRM. Protected video should show up black in clips.
- **Crash recovery:** if OBS crashes, ShadBuffer skips the safe mode question so recording comes back on its own. This also means you won't be offered safe mode if a plugin keeps crashing OBS.
- **Old OBS plugins:** an outdated plugin can make OBS show a plugin load error when it starts. Just click OK.
- **GPU support:** only tested on NVIDIA so far. AMD and Intel should work, but haven't been tested yet.

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download). The app targets .NET Framework 4.8, which is built into Windows.

```powershell
dotnet build ShadBuffer.csproj -c Release
```

The exe is written to `bin\Release\net48\ShadBuffer.exe`.

## Star History

<a href="https://www.star-history.com/">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=seandotnet/shadbuffer&type=date&theme=dark&legend=bottom-right" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=seandotnet/shadbuffer&type=date&legend=bottom-right" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=seandotnet/shadbuffer&type=date&legend=bottom-right" />
 </picture>
</a>

## License

[MIT](LICENSE). ShadBuffer is not affiliated with or endorsed by the OBS Project or NVIDIA.
