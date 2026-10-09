# Live watches — version 1.3.0

Open the console, enable Performance, and scroll down to **Watches** beneath Memory. Click the heading to collapse or expand it. Values update every 0.2 seconds by default and briefly highlight when changed. Hover a name or value to read its full text.

The wired demo automatically registers **Demo.Health** and **Demo.State**. Run `die` to see `0` / `Dead`, then `respawn` for `100` / `Alive`. The new demo behavior works on existing demo prefab instances too.

## Inspector setup: your health, ammo, speed, or connection state

1. Add **Console Variable Watches** to a scene object, typically your player.
2. Add entries to **Bindings**.
3. Set Label, drag your component into Target, and enter its public field/property name in Member (case sensitive).
4. Optionally set a numeric Format, such as `0.0` for speed.

Example: Label `Player.Health`, Target your player-health component, Member `Health`. A private serialized `health` field needs a public getter such as `public int Health => health;`, or use a code watch inside the owning component.

Inspector watches support public instance fields and readable public properties on MonoBehaviour components. They do not support nested paths, indexers, static fields, methods, or editing values. Disable/re-enable the binding component after changing entries in Play Mode. Invalid bindings produce one setup warning; getter failures display inline and can recover on the next sample.

## Code setup

Register on the main thread after the console exists. Keep the returned handle and dispose it when the owning object disables. This pattern also handles a console created later or replaced between scenes:

```csharp
using System;
using UnityEngine;

public class PlayerWatchExample : MonoBehaviour
{
    public int health = 100;
    public int ammo = 30;
    public float speed;
    private InGameConsole console;
    private IDisposable healthWatch, ammoWatch, speedWatch;

    private void Update()
    {
        if (InGameConsole.Instance == console) return;
        Release();
        console = InGameConsole.Instance;
        if (console == null) return;
        healthWatch = console.RegisterWatch("Player.Health", () => health);
        ammoWatch = console.RegisterWatch("Player.Ammo", () => ammo);
        speedWatch = console.RegisterWatch("Player.Speed", () => speed, "0.0");
    }

    private void OnDisable() { Release(); }
    private void Release()
    {
        healthWatch?.Dispose(); ammoWatch?.Dispose(); speedWatch?.Dispose();
        healthWatch = ammoWatch = speedWatch = null;
        console = null;
    }
}
```

Keep getters fast and free of side effects. They run on Unity's main thread in Update, never during GUI rendering. An exception affects only that watch, without generating repeated logs. No values are written to your game.

Watch Refresh Interval in the console Inspector controls sampling. Sampling pauses when the console is hidden, Performance is hidden, or Watches is collapsed; values refresh on subsequent visible sampling. The 0.8-second change highlight compares formatted display values. Updates faster than the sample interval can be missed.

Names are case-insensitive and unique. Registering the same name replaces its old getter; disposing an older handle does not remove the replacement. Use different labels for different players. The registry allows at most 128 watches. Disabling the Inspector binder removes its watches, avoiding stale scene references.

For IL2CPP builds with managed stripping, prefer the code API: direct getter references are visible to the linker. Reflection-only Inspector members may require preservation through your project's link.xml or Preserve attributes. Test your player build.

## Installation and verification

Use RuntimeConsole-UPM.zip and its package.json as before; package version is **1.3.0**. Back up custom package-code edits before replacing files. The new runtime files are InGameConsole.Watches.cs, RuntimeConsoleWatches.cs and ConsoleVariableWatches.cs; source-only upgrades require all three.

35 data-path tests cover the existing console plus watch updates, highlights, formatting, failure isolation/recovery, ownership replacement, cleanup, reentrant removal and capacity limits. Unity Play Mode interaction and scene lifecycle still require in-project testing; the local licensing issue prevented that verification.
