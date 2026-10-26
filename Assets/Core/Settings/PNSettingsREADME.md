# Machine settings

`PNUserSettings` / `PNSettingsStore` own schema5 for the local machine. The game
owner is `Assets/Game/Runtime/PNSettingsService.cs`; UI and Localization do not
write their own files. Storage is JSON, with no authored ScriptableObject asset.

Runtime path: `<Application.persistentDataPath>/Settings/settings.json`.

```json
{ "schemaVersion": 5, "locale": "vi", "targetFrameRate": 60,
  "mobileIconSet": "project_n:mobile_icons_shapes", "mobilePalette": "project_n:mobile_palette_u",
  "autoTargetMode": "nearest", "showPcControls": false, "showPcBindingLabels": true }
```

Locale is a validated code, allowed to be temporarily absent from the loaded
catalog. The default resolves from the base language manifest before saving.
Frame rate accepts30/60/90/120/144/0;0 means uncapped on PC. It is a requested
limit, not an FPS measurement or a guarantee that the hardware reaches it.

Schema1 is read with the built-in Shapes/U defaults and schema2 with its styles;
both read `autoTargetMode` as `nearest`, and the next explicit save writes schema5
(no write on load). `autoTargetMode` (schema3, 2026-10-25) is the player's auto
target rule: `nearest` | `lowest_health` | `lowest_health_percent`
(`PNTargetRule.ModeId`); `PNSettingsService.SetAutoTargetMode` saves it, the
Settings view shows it as the always-visible "Auto target" row (keys
`project_n:settings.auto_target[.<mode>]`, vi + en), and `PNTargetRules` gives it
to the local player's characters (see "Choosing a target" in PNCombatREADME). It
is machine-local and not part of coop parity; an `autoTargetMode` field inside a
schema2 file counts as unknown and protects the file. Legacy localization migration retains both old files. Locale/FPS/style
setters preserve all other fields. Unknown but valid style IDs are kept on disk;
the HUD resolves missing content to built-ins without rewriting the preference.

The bounded16KiB strict UTF-8 parser rejects duplicate keys, trailing documents,
wrong types and invalid values. Newer schemas and unknown fields protect
the file from writes. Load tries main then backup; a newer unsupported candidate
is not replaced by defaults. Missing both files permits one-way migration from
`localization.json` (then its backup). Legacy files remain; active code stops
writing them. Existing corrupt new settings do not reactivate stale legacy data.

Save validates, flushes a temp file and atomically replaces the main file. Before
replacing corrupt data it preserves a uniquely named `.corrupt-*` copy. A corrupt
main never overwrites a good backup. Reparse paths and newer files appearing
after load are rejected. Load itself never deletes or renames files. Save errors
are returned by the app service; the applied runtime choice remains in memory.

The app service loads before scene boot, survives EndPlay, and applies preferences
immediately. PC frame limits disable VSync; JSON0 maps to Unity targetFrameRate-1.
This favors explicit caps over VSync pacing. No ProjectSettings asset is edited.
On Editor Play exit or app shutdown it restores the FPS/VSync values it captured.
Mobile has a defensive native-refresh mapping for0, but its UI/device behavior is
not accepted by the PC verification. Mobile unlimited semantics need a platform
policy and corresponding label before release.

`showPcControls` (schema5) is a strict Boolean, default false for schemas1–3.
Loading an older schema does not write it; the next explicit save migrates to5.
A `showPcControls` field in schemas1–3 is unknown and protects the file. Every
immutable With* method preserves the other preferences. `SetShowPcControls` in
the app service is the sole writer for the desktop action HUD choice; it uses the
same machine-local file, backup and protected-save behavior. It is not coop parity.
Desktop Settings exposes this choice and the existing shared icon/palette rows;
mobile does not expose the PC toggle. Requested style IDs are shared across both views.

Localization keeps its requested/effective distinction. Catalog unload or fallback
never saves over a preference; a returning locale becomes effective again.
`PNLocalization.Select(code, false)` remains temporary and does not save. With
persist=true it routes through the shared service. Tests inject an isolated store;
Development Players can use `-pn-settings-fixture=<root>`, ignored by release builds.

See `Docs/SettingsImplementationReport.md` for actual evidence and limitations;
the plan/guide describe the implementation sequence, not historical PASS evidence.

`showPcBindingLabels` (schema5) is a strict Boolean, default true on schemas1–4.
It independently hides the five PC keycaps; icons and press/hold feedback remain.
`SetShowPcBindingLabels` uses the same writer/store. Every With* method preserves it;
read of an older schema never writes, explicit save upgrades to5. Its appearance in
schemas1–4 is unknown and protects the file, as do newer schema versions.
Settings now groups General, Combat and Interface vertically, at most two columns
inside each section, labels left and dropdowns right. Headers have a soft underline;
section dividers have a centred star. No heading constellation or scrollbar bling.
Scroll thumbs appear at alpha.5 on scroll, fade150ms after600ms idle. All view content
and decoration share a760ms opening and700ms closing. The view keeps its own unscaled
clock and owned animation state; it schedules no delayed scroll effects.
