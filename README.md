
<img width="960" height="640" alt="gif" src="https://github.com/user-attachments/assets/e11e6b74-6375-47e0-9591-6004f6eb3539" />

shadowplay keeps turning itself off on me because of drm errors so i made this. one click sets up obs to work like instant replay. it sits in the tray and saves your last X minutes when you press a hotkey.

**install**

download `ShadOBS.exe` below, run it, click set up and accept the admin prompt. or paste this in powershell:

```
irm https://raw.githubusercontent.com/seandotnet/ShadOBS/main/install.ps1 | iex
```

windows might show a smartscreen warning because the exe isnt signed. click more info then run anyway. needs windows 10 or 11.

**how it works**

- alt+f10 saves a clip, alt+x opens the overlay
- clips go in videos\clips, sorted into a folder per game
- the overlay has recording on/off, clip length, hotkeys, quality, fps, resolution, encoder, mic selection, mic noise suppression and startup options

**what it changes on your pc**

- installs obs if you dont have it
- adds its own obs profile and scene collection, your other profiles stay as they are
- starts obs in the tray at login, running as admin (you can turn that off)
- turns off game bar capture and the obs exit warning
- remove in the app puts all of that back, your clips and obs itself stay

**read this first**

- game capture hooks into fullscreen games so some anti cheats may block it or object. switch to desktop capture in the overlay for stuff like valorant, cs2 or faceit, that one doesnt touch the game
- desktop capture records the whole monitor so notifications and the overlay can end up in clips
- your mic is recorded by default, turn it off in the overlay if you dont want that
- it doesnt get around drm, protected video should just show up black in clips
- if obs crashes it skips the safe mode question so recording comes back on its own, which means you wont get offered safe mode if a plugin keeps crashing obs
- if you have an outdated obs plugin, obs might show a plugin load error popup when it starts, just click ok
- only tested on nvidia so far, amd and intel should work but i havent been able to test them

not affiliated with obs or nvidia.
