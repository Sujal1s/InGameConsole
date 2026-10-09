# Runtime Console — Package Manager version

## Install from GitHub

In Unity Package Manager, choose **+ > Add package from git URL** and enter:

https://github.com/Sujal1s/InGameConsole.git


Version 1.3.0 adds live variable watches. See [WATCHES.md](WATCHES.md) for Inspector setup, code examples, and the wired demo.

## Install with Add package from disk

1. Extract RuntimeConsole-UPM.zip into a permanent folder OUTSIDE your project's Assets folder.
2. Open Unity > Window > Package Manager (Window > Package Management > Package Manager in some versions).
3. Choose + > Add package from disk (also called Install package from disk).
4. Select com.local.runtime-console/package.json inside the extracted folder.
5. Wait for compilation, then choose Tools > Runtime Console > Create Wired Demo.
6. Enter Play Mode. Type die, respawn, or heal 25.

Select package.json, not package-checks.json, an .asmdef, a ZIP, or a .unitypackage. Keep the extracted directory: Unity references it in place. Moving or deleting it breaks the local dependency.

If you imported the old asset package, back up your edits and remove the old Assets/RuntimeConsole scripts/components before installing this version, so there is only one definition of each class. Recreate the console using the Tools menu. Do not install both versions together.

## Your own player death function

Use Tools > Runtime Console > Create Console. Add GameConsoleCommands to your player or a scene-local manager. In On Die, click +, drag in the player, and choose its public void Die() method. Typing die invokes that event. The demo uses a demonstration player; your own game needs this connection.

Edit Runtime/GameConsoleCommands.cs in this local package if you prefer a direct code call. For maintainable game-specific code, keep your player script in Assets and bind its function with the Inspector event. Add further commands to Additional Commands. Select Dynamic string callbacks to receive command arguments.

If your game scripts use an assembly definition, add a reference to RuntimeConsole.Runtime when they use its types. RuntimeConsole.Editor is Editor-only. The small demo class lives in Runtime/Demo and only runs when added to an object.

## Included

- Suggestions with usage, descriptions, and clickable argument examples.
- Performance sidebar: FPS, last 240 frames, mean/peak frame time, and Unity memory counters.
- Log search/filtering/grouping, selectable details, copy/export, history, dragging/resizing.
- Plain and wired-demo prefabs under Runtime/Prefabs, also available through the Tools menu.

Backslash toggles. Enter runs. Tab fills suggestions. Up/Down selects matching commands; Alt+Up/Down recalls history. Example fills without running. Escape dismisses suggestions, then closes. Metrics toggles the sidebar.

Gate gameplay input while the console is open. Development Builds Only defaults to enabled. Preferences are session-only. This is a desktop debugging UI.

## Validation and compatibility

Package structure, manifest, assembly separation, prefab GUID references, and event assembly names are checked. Runtime and Editor source are compiled against installed Unity 6000.6.0f1 assemblies; 35 data-path tests passed. The manifest minimum is Unity 2021.3; older-version runtime behavior has not been tested. Unity's licensing connection prevented Package Manager import and Play Mode verification in this environment.

