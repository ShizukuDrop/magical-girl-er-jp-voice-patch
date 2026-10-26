# Magical Girl ER — JP Voice Always-On Patch

A tiny patch for [Magical Girl ER](https://store.steampowered.com/app/4395870/) (Steam) that makes the
**Japanese character voices play in every game language**.

By default the game only plays its Japanese voice lines when the UI language is set to Japanese.
This patch removes that restriction: keep the game in **English** (or Simplified Chinese) and still
hear all the JP voices — in conversations and in the character voice gallery.

- Text localization is untouched: English stays English, Chinese stays Chinese.
- One small file is modified: `Assembly-CSharp.dll` (the game's C# code). No game assets are changed
  or redistributed, and this tool contains none of the game's data.
- Easily reversible (see Uninstall).

> **You must own the game on Steam.** This is a client-side mod for people who own the game, not a
> way to play without it.

## Install

1. Make sure the game is **not running**.
2. Download this repository (green **Code** button → *Download ZIP*) and unzip it.
3. Open the unzipped folder → `dist` and double-click **`MagicalGirlER-JPVoicePatch.exe`**.

That's it — the tool finds your Steam install automatically (including games on other drives),
backs up the original file, and applies the patch. You should see:

```
Game: C:\Program Files (x86)\Steam\steamapps\common\Magical Girl ER
[OK] Patch installed. Japanese voice now plays in every language.
```

Close the window and launch the game. Voices now play in every language.

**If Windows SmartScreen warns** ("Windows protected your PC"): click *More info* → *Run anyway*.
That warning appears for all unsigned downloaded programs and is not specific to this tool.

### Non-standard install location?

Drag the game's install folder (the one containing `Magical Girl ER.exe`) — or the `.exe` itself —
onto `MagicalGirlER-JPVoicePatch.exe`.

### Other commands

Open a terminal in the `dist` folder (or run it from cmd/PowerShell):

| Command | What it does |
|---|---|
| `MagicalGirlER-JPVoicePatch.exe` | Install the patch (default) |
| `MagicalGirlER-JPVoicePatch.exe status` | Show whether the patch is installed |
| `MagicalGirlER-JPVoicePatch.exe uninstall` | Restore the original file |
| `MagicalGirlER-JPVoicePatch.exe help` | Usage information |

## Uninstall

Any of these works:

- Run `MagicalGirlER-JPVoicePatch.exe uninstall`, **or**
- In Steam: right-click the game → *Properties…* → *Installed Files* → **Verify integrity of
  game files**. (Steam restores everything, including the backup file.)

The installer leaves the original file next to the patched one as
`Magical Girl ER_Data\Managed\Assembly-CSharp.dll.orig_backup` so the patch can be undone offline.

## Troubleshooting

| Problem | Fix |
|---|---|
| "Could not find the game" | Is it installed on this PC? Otherwise drag the game folder onto the exe (see above). |
| "The game is running" | Fully close the game (also check the system tray), then retry. |
| "does not match the game build the patch was made for" | The game was probably updated. The patch refuses to touch files it doesn't recognize rather than break the game — see *Keeping it updated* below. |
| Voices still silent in English | Run `status` to confirm the patch is installed, check the in-game voice volume slider, and make sure you're past the prologue scenes. |
| Game updated and the patch stopped working | See *Keeping it updated* below. |

## Keeping it updated

This patch is written against the game build from **October 2026** (Steam app 4395870). If the
developer ships an update that changes the game's code, the patcher will detect the new file
version and **refuse to modify it** instead of risking a broken game. When that happens:

1. Get the updated build (let Steam update the game).
2. Check this repository for an updated version of the patch.

## How it works (for the curious)

The game is a Unity/Mono game; its logic lives in `Magical Girl ER_Data\Managed\Assembly-CSharp.dll`.
The Japanese voice clips are always present in the install — the code simply refuses to play them
unless `GameLanguage` is Japanese. The patcher (built with [Mono.Cecil](https://github.com/jbevain/mono.cecil))
rewrites four spots in the compiled C# (IL) so the language gate always passes:

1. `GalleryManager.IsJapaneseGalleryLanguage()` → always returns true (voice gallery works everywhere)
2. `PatientManager.StartPatientCharacterVoiceWithDelay()` → language check removed (dialogue voices)
3. the delayed-play coroutine (`PlayPatientCharacterVoiceAfterDelayRoutine`) → language check removed
4. `PatientManager.RefreshLanguageFromSettings()` → no longer stops voices when you switch language

Every edit is pattern-verified: if the code doesn't look exactly as expected, the tool aborts
without touching the game.

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (the game itself is not needed to build):

```
cd VoicePatch
dotnet build -c Release
```

Output: `VoicePatch\bin\Release\net48\MagicalGirlER-JPVoicePatch.exe` (targets .NET Framework 4.8,
which is preinstalled on Windows 10/11 — no runtime to install).

## Disclaimer

Fan-made mod, not affiliated with the developer or studio. Use at your own risk — though the worst
that can happen is a game file mismatch, which Steam's *Verify integrity of game files* fixes in
seconds. The patch and the original file backup will be overwritten by game updates.
