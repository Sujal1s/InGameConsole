# Version 1.2.0 — interface redesign

This version keeps the commands, callbacks, log capture and performance sampling, and revises the presentation.

## Visual changes

- Warm charcoal surfaces, pale neutral text, and restrained sand-colored active indicators.
- Sans-serif UI labels and monospace timestamps, messages, command input and examples.
- Segoe UI / Helvetica Neue / Arial interface font fallbacks; Cascadia Mono / Consolas / Menlo / DejaVu Sans Mono code font fallbacks. No operating-system font files are redistributed. An assigned Console Font overrides both families. Actual font rendering varies by platform.
- Larger Console heading and FPS readout, with quieter secondary labels.
- Column labels and fine row separators instead of alternating blocks. Errors have a soft tinted row.
- A wider, proportional performance sidebar, aligned memory values, a quieter frame graph and a dashed target guide.
- Thin scrollbar styling, restrained filter tabs, and fewer instructions competing with the content.
- Hover Memory to read the counter explanation. The full explanation also remains in the setup documentation.

## Upgrade a local Package Manager installation

1. Back up any edits made inside your existing package, especially GameConsoleCommands.cs.
2. Extract RuntimeConsole-UPM.zip to a permanent folder outside Assets.
3. In Package Manager use Add/Install package from disk and select that folder's package.json. The package name is unchanged and its version is 1.2.0.
4. Keep one installation of the console. Do not also import the .unitypackage into Assets.
5. Existing component settings remain serialized. Reapply any custom command-code edits; Inspector event bindings continue to use the same script GUIDs.

If your local dependency already points at the supplied outputs/com.local.runtime-console folder, its files have been updated in place. Let Unity refresh and recompile; there is no need to add a second copy.

Suggested starting size: 95% width, 80% height, UI Scale 1. The Console Font field is optional. Control/log text has a readability floor even on existing components with the older font-size default.

## Preview and verification

Console-redesign-preview.png is a layout illustration with sample data, not a screenshot captured from Unity. It previews the typography, spacing and palette; OS font fallback and screen scaling may change the runtime result.

The updated source passed 25 data-path checks and compilation against Unity 6000.6.0f1 assemblies. Package archives and references were checked. The redesigned UI has not been exercised in Unity Play Mode because the local Editor licensing connection failed during the earlier validation attempt.
