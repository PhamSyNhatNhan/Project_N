# Mobile controls style API

Two immutable registered types: `PNMobileIconSetDefinition`, `PNMobilePaletteDefinition`.
Use `context.Register(definition)` in a DLL mod's `Register`, just like other content.
Game built-ins use `PNMobileUiContentModule` and the same typed registries.
Runtime accessors: `hub.MobileIconSets()`, `hub.MobilePalettes()` in EveEngine namespace.
No custom loader, ScriptableObject asset, arbitrary SVG parser or texture is required.

Arrays contain exactly five entries in `PNMobileControlChannel` order:
Attack, Skill, Ulti, Dash, Burst. Icons contain 1..8 paths, each 2..128 commands;
each starts with one Move, may end with Close, and has at least one drawing command.
Coordinates and RGB are finite0..1. Stroke width is normalized0..0.2, positive
unless filled. The renderer uses nonzero fill and rounded line caps/joins.
Quadratic stores control XY and endpoint X2Y2; Cubic stores controls XY/X2Y2 and
endpoint X3Y3. Fill/stroke is white; colour belongs to the icon halo only.
Definitions clone input collections and expose read-only snapshots. No Unity objects,
callbacks, changing state or renderer resources belong in definitions.

IDs must belong to the registering mod; a foreign localization key namespace needs
a declared dependency. Missing text falls back through Localization's normal policy.
Definitions cannot register after content seal; no stock `base:` override points are
published in this first version. Add another selectable set rather than replacing a
player's choice. Registry names for binding selection are `mobile_icon_sets` and
`mobile_palettes`; their presence does not grant an arbitrary override contract.

Stock sets: `project_n:mobile_icons_shapes` (default), `project_n:mobile_icons_letters`.
Stock palettes: `project_n:mobile_palette_u` (default), `_d`, `_a`, `_k`.
`PNMobileStyleDefaults` is the single source for pre-content Settings and registration.
Catalog uses a content-generation token so old cleanup cannot clear a newer catalog.
EndPlay retains content; unload returns to stock. Preferences retain requested IDs
when a mod is absent; returning content resolves them again. HUD updates existing icon
views, leaving input controls, captures and outer button glow/rings intact.

Settings stay in `persistentDataPath/Settings/settings.json`, schema2. The game service
owns writes; mods register options and never set the player's preference automatically.
Content/pack/DLL hashes retain the existing compatibility policy; different local
selections do not change those hashes. Android/iOS still do not load mods.

Example: `Examples/Mods/MobileControlsStyleMod`. Build after exporting a fresh SDK:

```powershell
./Tools/Mods/ExportModSdk.ps1 -UnityEditorPath <Unity.exe> -OutputDirectory <SDK>
./Tools/Mods/BuildMobileControlsStyleMod.ps1 -SdkDirectory <SDK> -OutputDirectory <pack>
```

The script builds a pack with its DLL and vi/en translations. Put that pack under
the existing Mods folder and select it through existing enabled-pack selection.
Desktop can preview mobile HUD through `PNUiRuntime.ShowMobileControls`; enabling
that preview does not alter platform mod policy. No claim of mobile device verification
follows from running the desktop fixture.
