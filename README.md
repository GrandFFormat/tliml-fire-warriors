# TLIML Fire Warriors

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin for **This Land Is My Land** (Unity 2018.2.21f1, Mono backend).

Craft a "Fire Warrior Campfire" and plant it anywhere you want to raid. Five fast, unarmed
warriors rise from the fire and fan out 100m around it. They strip every loose item, chest
and crate, and seize every horse and wagon. Everything goes straight to your home camp, then
they walk back into the fire and vanish.

Full feature list, every config option and the complete changelog:
[`docs/README_FireWarriors.txt`](docs/README_FireWarriors.txt).

## Contents

| Path | What it is |
| --- | --- |
| `src/FireWarriorsPlugin.cs` | The entire mod. One file, no other source. |
| `build.ps1` | Build script (Windows, no SDK required — see below). |
| `docs/README_FireWarriors.txt` | User-facing readme: features, config, changelog. |
| `docs/MODDING-NOTES.md` | Reverse-engineering notes on the game's internals. |

## Install

Drop the built `FireWarriors.dll` into `<game>\BepInEx\plugins\` and launch normally.
Requires BepInEx 5.4.x. Config is written to
`<game>\BepInEx\config\local.tliml.firewarriors.cfg` on first run.

## Building

**No .NET SDK, Visual Studio or MSBuild is required.** The build has one prerequisite: a copy
of the game, because the plugin compiles against the game's own assemblies. Those assemblies
are the game's property and are deliberately **not** redistributed in this repository.

```powershell
.\build.ps1
# or, to build and copy straight into the game's plugins folder:
.\build.ps1 -Deploy
```

Pass `-GameDir` if the game isn't at the default Steam location:

```powershell
.\build.ps1 -GameDir "D:\Steam\steamapps\common\This Land Is My Land"
```

Output: `build\FireWarriors.dll`.

### How the build works (and why it looks unusual)

The mod source is C# 7, but the only compiler present on a stock Windows machine is the
in-box `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, which is pre-Roslyn and
understands C# 5 only.

Rather than require an SDK install, `build.ps1` uses **Roslyn 2.10, which the game itself
ships** in `This Land Is My Land_Data\Managed\` (`Microsoft.CodeAnalysis.dll` and
`Microsoft.CodeAnalysis.CSharp.dll` — the game embeds RoslynCSharp for its own runtime
scripting). The script drives that compiler through late binding and emits the plugin DLL.

It references, all from the game's own `Managed` folder plus `BepInEx\core`:
`mscorlib`, `System`, `System.Core`, `UnityEngine` + the `CoreModule` / `AIModule` /
`ParticleSystemModule` / `PhysicsModule` / `IMGUIModule` / `TextRenderingModule` /
`AnimationModule` assemblies, `Assembly-CSharp`, `Assembly-CSharp-firstpass`, `BepInEx.dll`
and `0Harmony.dll`.

`build.ps1 -Deploy` refuses to copy into `BepInEx\plugins` while the game is running: BepInEx
loads plugin DLLs from bytes rather than mapping them, so the file isn't locked and the copy
would silently succeed while the running game kept the old code.

## What the mod does to the game

Nothing on disk. No game file is modified, patched or replaced. At runtime the plugin:

- Adds one new craftable item by cloning an existing in-game campfire item and appending a
  recipe entry to the game's own recipe list.
- Spawns NPCs through the game's own `AINavMeshHumanoid.CreateHumanoid` factory and drives
  them with its own `NavMeshAgent` commands.
- Uses three small Harmony postfix hooks: two on per-frame `Update` methods purely to get a
  reliable per-frame tick, and one that intercepts *only* this mod's own item when used.
  None of them change what the hooked methods do.

Details, including the game-internals research behind each of those, are in
[`docs/MODDING-NOTES.md`](docs/MODDING-NOTES.md).
