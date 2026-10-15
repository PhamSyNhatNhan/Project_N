# Damage popup / Số damage

VI: Popup thuộc Core và được PNStat tạo sau khi ghi HP, trước DamageReceived/Died.
PNPlayerStat.CanShowDamagePopup trả false: player nhận damage không có popup;
player gây damage lên enemy vẫn hiện ở enemy. Không cần gắn script lên slash.

EN: Core PNStat owns popup presentation and its per-receiver pool. Spawn occurs
after the health commit and before observers can destroy the receiver. Player
receiver suppression is implemented by PNPlayerStat, not by attacker filtering.
Presentation failures are logged without undoing or retrying committed damage.

## Authoring

- Default prefab: Assets/Resources/PNUI/PNDamagePopup.prefab, with 3D TextMeshPro
  and PNDamagePopup. PNStat's Damage Popup Prefab field can select another prefab;
  leaving it empty loads this default. DamagePopupPrefab is also code-settable.
- PNStat Inspector: Show Damage Popups, Damage Popup Offset, physical/magic/true
  colors. CanShowDamagePopup and DamagePopupColor are virtual extension points.
  PNPlayerStat always suppresses received-damage popups in the current design.
- PNDamagePopup Inspector: Lifetime (.55 seconds), Rise (.45 units), Glow Intensity
  (7), Critical Glow Intensity (7), Fade Start (.65), Scale curve. Base prefab scale
  is .4; Critical Scale Multiplier1.125 makes critical base scale.45. The existing
  pulse curve multiplies these scales. Edit TMP font size/alignment and the material on the
  prefab. Numbers face Camera.main, rise, shrink and fade. Three alternating lanes
  separate consecutive hits without consuming gameplay random state.
- Pool API: PNStat.DamagePopupPool exposes active/inactive counts and active
  instances. Completed instances are reused; up to 32 idle instances are retained.
  Each delivered hit gets its own popup, without aggregation or cooldown.

## Value and lifetime

Display CalculatedDamage after crit/DEF/mitigation, including full overkill. It is
not AppliedDamage: overkill can exceed remaining HP. Stats and damage now use double;
at200M HP, calculated10 removes exactly10. Popup rounds to a whole number using
MidpointRounding.ToEven, like original Mathf.RoundToInt:37.5 ->38,1.25 ->1. BigInteger
formats the rounded double without Int32 overflow or Mono's15-digit formatting loss;
damage/HP calculations remain double. The stat Inspector retains two decimals. Zero
damage on a living receiver displays 0; hits on already-dead receivers do not
spawn popups. Critical-hit metadata is read from PNDamageResult.IsCritical.

Layout: [critical sprite, if applicable] [integer damage] [all elemental sprites].
Number colors remain Physical cyan / Magic pink / True white. Normal and critical numbers use the same configured full color and HDR intensity (7), giving equal Bloom brightness for a given damage type. Every registered element tag is selected,
deduplicated and sorted by canonical EveId. Default fire/ice/lightning icons have
their own orange/blue/yellow colors. Crit icon is a hollow four-point sparkle at the front, tinted to match the damage number. Fire and lightning use hollow outlines; ice uses snowflake line art.

PNDamagePopupIcons.asset references a five-glyph TMP sprite atlas and a separate
HDR sprite shader/material, avoiding dependency on emoji font glyphs. All submesh
renderers receive the same emission/fade properties. Elements[] on the popup
maps canonical element IDs to sprite names/colors; code/mods can extend it and the
Sprite Asset. Unmapped mod elements display a generic colored icon plus full ID,
so multiple unknown elements remain distinguishable. Text wrapping is disabled. Element Icon Scale defaults to .8: size/vertical-offset tags shrink element glyphs and their spacing while preserving their centre height; the crit prefix retains its original size.

The receiver's EveTimeScale controls the animation while attached. Unload or
destruction retires the pool, detaches that clock and lets active popups finish.
Session end/change invalidates the pool. Direct SetCurrent does not create popups.
Native Edit Mode fixtures must opt in through PreviewDamagePopupsInEditor.

## Real glow

PNDamagePopupSDF.shader emits HDR color via MaterialPropertyBlock; it does not
duplicate a material per popup. PNDamagePopup.mat uses the existing TMP SDF atlas.
Glow/opacity stay at full strength for the first65% of the lifetime, then fade
together using SmoothStep over the remaining35%. This is per-popup shader fading,
not animation of the camera's global Bloom intensity. Sprite and number glow are
both covered by this lifecycle. The text uses a thin dark SDF stroke (_OutlineWidth .12); HDR emission applies to the face before outline composition, so the stroke stays dark.

Bloom uses Resources/PNUI/PNDamagePopupBloom.prefab and its VolumeProfile asset
(intensity 1.2, threshold 1.1, scatter .65). On the first visible gameplay hit,
PNDamagePopupBloom enables HDR, URP post-processing and per-frame Volume updates
on Camera.main, then creates one session-owned global Bloom Volume. No gameplay
scene asset is rewritten. This Volume has priority -100; higher-priority scene
Volumes can override it. Bloom also affects other HDR content visible to that
camera, so tune the profile alongside scene lighting when art is finalized.

This is local Offline/Host damage presentation. Client damage-result transport is
not implemented by this change. Core now references TMP and URP runtime assemblies;
the Mod SDK exporter includes those compile-time references.

## Verification

PNDamagePopupEditorChecks.Run():24 native checks, including ten hits, exact double
HP loss, integer rounding above Int32, critical scale/layout, all elements and
unknown-element fallback, submesh HDR, pool reset, pause and a death
observer unloading/destroying the receiver.

PNDamagePopupRenderChecks.Run(): disposable preview scene and full URP
StandardRequest rendering; checks HDR pixels >1 and Bloom outside the complete
crit/three-element popup at spawn, held middle and fading end of life.
Writes .utmp/PNDamagePopupGlow.png. SingleCameraRequest does not update Volume state
in this URP version and must not be used for this Bloom verification.

PNDamagePopupAuthoring.CreateDefaults() creates missing default assets only;
existing authored assets are preserved.
PNDamagePopupIconAuthoring.Apply() creates missing icon assets and applies the
agreed .4/.45 scales, icon reference, no-wrap and FadeStart to the existing prefab.
