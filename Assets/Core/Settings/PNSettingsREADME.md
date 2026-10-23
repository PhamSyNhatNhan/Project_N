# Machine settings

`PNUserSettings` / `PNSettingsStore` own schema2 for the local machine. The game
owner is `Assets/Game/Runtime/PNSettingsService.cs`; UI and Localization do not
write their own files. Storage is JSON, with no authored ScriptableObject asset.

Runtime path: `<Application.persistentDataPath>/Settings/settings.json`.

```json
{ "schemaVersion": 2, "locale": "vi", "targetFrameRate": 60,
  "mobileIconSet": "project_n:mobile_icons_shapes", "mobilePalette": "project_n:mobile_palette_u" }
```

Locale is a validated code, allowed to be temporarily absent from the loaded
catalog. The default resolves from the base language manifest before saving.
Frame rate accepts30/60/90/120/144/0;0 means uncapped on PC. It is a requested
limit, not an FPS measurement or a guarantee that the hardware reaches it.

Schema1 is read with the built-in Shapes/U defaults; the next explicit save writes
schema2. Legacy localization migration retains both old files. Locale/FPS/style
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

Localization keeps its requested/effective distinction. Catalog unload or fallback
never saves over a preference; a returning locale becomes effective again.
`PNLocalization.Select(code, false)` remains temporary and does not save. With
persist=true it routes through the shared service. Tests inject an isolated store;
Development Players can use `-pn-settings-fixture=<root>`, ignored by release builds.

See `Docs/SettingsImplementationReport.md` for actual evidence and limitations;
the plan/guide describe the implementation sequence, not historical PASS evidence.
