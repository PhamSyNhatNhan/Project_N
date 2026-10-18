## Current: Skill binding visibility (RulesVersion75)

Local HUD filters Ability IDs associated with cached binding profiles to the selected
profiles, retaining entity-wide states. This changes visibility only; hidden cooldowns
keep their entity-owned clocks. No new skill bar/layout/style or remote Ability display.


## Current: Buff reliability and presentation coop (RulesVersion66 / Mod API5)

Buff registration requires explicit ImplementationId; Derive preserves it. Buff and
Area reference schemas use validateReferences/PNContentReferenceContext, including
dependency/alias/staged batch checks; property-name inference was removed. Nested
Action/Operation schemas may delegate their registered reference callbacks.
Stat resource max callbacks run only when max actually changes. Buff stat-source
keys use checked instance+sequence, retained through behavior changes/resume.
Automatic Buff manager clock/runtime failures retire only its collection and cleanup;
caller argument errors and per-behavior callback errors retain separate handling.

Buff gameplay is authoritative. Host/solo and client UI/VFX use a common replica
presentation source; UI Numbers and VFX clock data are separate. Elemental VFX is
presenter-owned/grouped, not acquired by gameplay behavior. No production transport,
Move/Skill/HP/Area replication is implied. See Assets/Core/Entity/Shared/Buff/PNBuffCoopREADME.md for epochs/sequences,
resync/cues/presenter scope and trust limits. Earlier no-replication statements refer
to the former local contract; the new transport-independent proxy is implemented.

# Combat UI

Floating UI follows the current pause UI: light palette, rounded forms, thin
borders and subtle glow. Buttons use the pause menu buttons as the reference for
appearance and interaction states; opening/closing motion should feel consistent.
Decorative star icons may use the plump rounded hollow pause-toggle shape,
sparingly. Keep pn-panel-surface for panel/window backgrounds. This standard does
not authorize automatic changes to existing UI or its filled HUD/skill glints.

`EveGameRuntime.Initialize` attaches `PNUiRuntime` during Play Mode after the
content session is ready. No authored scene or Character Visual is rewritten.
Resources `UI/Combat` contain the independent UI Toolkit UXML/USS/theme.

UI design rule: light/white is the primary palette, following the local HUD.
New panels/windows use the default background RGBA(184,192,196,0.97)
(#B8C0C4 at97% opacity). Apply the shared `pn-panel-surface` class from CombatUI.uss.
Inner cards remain near-white; the overlay HUD remains transparent.
Use minimal rounded surfaces and dark text where a light surface needs contrast.
Scrollbars are slim pill thumbs inside cards with no visible track/frame/arrows.
Buff PNG artwork retains its original shape.

- Local HUD: circular portrait with inner white border and a second outer 1px
  white ring at45% opacity, separated by2px. White pill HP track
  (210x14 reference pixels, no HP text), with real30x30 unrounded PNG Buff icons below.
  No panel background, player name or Buff caption. Empty portrait uses a neutral
  silhouette. Each live visible Buff instance has its own icon; missing artwork
  uses Resources Effects/Burn/Burn. PNG alpha/colors are preserved. Buffs fill
  five columns left-to-right/top-to-bottom, showing all instances. Same canonical
  IDs are adjacent by first-appearance group order and instance admission order.
  Eve currently uses `UI/Portraits/Eve_Icon`, moved from the user-selected
  Sakuya_Icon PNG with its original GUID. The legacy Sakuya data path also follows
  this moved asset so its existing icon loading remains valid.
  Portrait presentation trims transparent margins at runtime (alpha >=16/255),
  caches a Sprite view on the source texture, then uses cover scaling inside a
  circular clip. Source PNG/imports stay unchanged. Packed sprites use their
  original Sprite view. GPU readback temporaries are released immediately; owned
  Sprite views are released on disable/destroy/session replacement.
  A narrow, low-opacity white vector halo sits behind the avatar and remaining
  HP fill; zero HP has no fill halo. It never intercepts input or affects Buffs.
  Avatar halo follows the outer ring and is slightly dimmer to keep both rings distinct.
- Enemy world panel: real unframed30x30 PNG Buff icons above a white
  rounded90x8 HP bar for every `PNEnemyStat`, including ordinary enemies at full
  health. No name, HP numbers or panel background. Buffs run left-to-right and
  wrap bottom-to-top in five columns, with a4px gap to
  the HP outer ring. The transparent96x14 pill ring has a1px light-red border
  (RGBA255,180,180,0.8) for enemies and2px spacing around HP. Remote players reuse
  HP + Buff presentation with the original white ring at45% opacity and no Ability
  icons. Local player HUD/Ability presentation and all Buff artwork stay unchanged.
  The document is200px wide with dynamic Buff
  height; the90x8 HP and96x14 ring stay centered and do not grow with the grid.
  Bottom-center anchor projects the Sprite quad top with a6 reference-pixel gap;
  enemyOffset is the fallback without a SpriteRenderer. HP reads current/max health.
- Player world panel:35x35 PNG Ability icons, no name/card background/frame/progress
  bar. Image opacity45% when unavailable,100% when available; numbers stay full
  opacity. One icon per visible ID, sorted by displayOrder/ID,
  inactive IDs are hidden unless an actual instance has cooldown remaining>0;
  hidden icons leave no layout slot. This checks running cooldown, not just Kind.
  Active icons remain visible, including those with charges while recovering.
  left-to-right then bottom-to-top. Missing images use the original SkillIcon
  placeholder copied into Resources/UI/Combat/AbilityPlaceholder.
  Cooldown charge bottom-left when maximum>1; Counter amount top-right when
  maximum>0. Remaining cooldown/duration bottom-right, rounded up to seconds,
  hidden at zero (permanent States have no timer text). Reads passive Ability;
  never runs a separate UI timer. Numbers use16px bold white text without outline,
  on a soft grey capsule badge: a signed-distance alpha mesh fades continuously
  over5px from30% central opacity to0% at edges, without concentric fill layers.
  Text is a separate child and stays sharp;
  padding1px vertical/3px horizontal. No texture/material ownership. Empty badges hide.
  Right-side labels align left; left-side charge aligns right. Numbers sit inside
  their respective corners vertically with1px inset. The badge's left edge anchors
  at the right icon edge minus15px (including3px text padding), so right-side digits grow outward to the right;
  left charge uses the opposite edge and grows outward to the left. Only12px of
  each text box overlaps the icon (about10–11px of visible glyph), independent of digit count.
  Anchor projects the actual character Sprite quad top with a6 reference-pixel
  gap; fallback uses playerOffset when no SpriteRenderer is available. Panel grows
  upward from bottom-center. Enemy panel and local HUD remain separate.
- Collapsible test panel: entity selector, evaluated/base stats, iframe and time
  scale, Eve cast/phase and individual Ability instances/timers/clocks. Read only.
  Light grey panel (184/192/196,97% opacity), near-white opaque cards (250/251/252),
  darker readable text (28/33/37) and bold11px section headings. Three soft rounded
  sections (Live/Stats/Abilities), white4px pill scrollbar without track/frame/arrows,
  inset within the clipped panel. Height is capped to
  the viewport; content scrolls. Data and selection behavior remain unchanged.

## Buff presentation lifecycle (RulesVersion61)

PNBuffView binds one collection, subscribes PresentationChanged and bootstraps from
PresentationSnapshot cache once. No per-frame behavior/snapshot number queries.
Cards retain handle identity; layout rebuilds only when card membership changes,
while animation and camera follow continue each frame. Group order survives oldest
instance removal; an emptied ID group joins the end when reintroduced. A group may
cross a row boundary. Gameplay instances/sources are never merged for display.

Four optional integer badges occupy the four corners. Null hides,0 remains visible;
Buff chooses contents. Current DOT stack1 is shown. White14px sharp text and the
Ability-style soft grey capsule are adjusted to30px artwork:10px horizontal overlap,
16px text height and2px outward vertical offset avoid four-corner crowding. PNGs
remain unrounded, unframed and unmasked. Ability icons/badges remain unchanged.

New instances fade in .12s with one .2s icon/white-halo pulse (3/35 proportional
growth). Initial cache and resume/profile updates do not pulse. Removed icons fade
.1s, then retire; neighbors reflow over .2s. Number changes never bounce/pulse.
Suspend dims only artwork to45%; numbers/order stay. UI animation is unscaled,
Buff timer is source-clock-owned. Offline pause/hidden views do not replay pending
appearance pulses. Disable/rebind/unload detach subscriptions and cached UI cards.

PNBuffUIPlayReview.Run in Ready unpaused Play creates disposable player/enemy
fixtures, verifies five-column down/up layout,11/5/0 instances, full four-number
updates, Burn fallback, input picking transparency, suspend and cleanup. It records
GameResults/SimulatorResults and actual selected-view frames in .utmp/BuffUIReview.
No authored scene is saved/reloaded; physical phone ergonomics remain unverified.

## Mobile controls

Toolkit controls are attached to the existing HUD. Mobile platforms show them
automatically. In Editor, `PNMobilePlatform` checks Unity's remembered main Play Mode
view before using the Device Simulator mobile shim: Simulator shows controls,
regular Game hides them. Inspector/Scene/Console focus preserves the selected view.
`PNUiRuntime.ShowMobileControls` explicitly enables Editor/PC preview.
The isolated Editor adapter uses Unity6000.6's internal GetMainPlayModeView API;
if that API is unavailable it defaults to desktop. Builds use Application.isMobilePlatform.
Five fixed placeholder PNG buttons have no labels, charges or cooldown
numbers. Their positions/size ratios follow the original ErMode cluster (Attack
largest; Skill/Ulti above; Dash below-left; Burst upper-left). Centers retain the
original authored2273px canvas to1280px reference conversion; revised diameters
are Attack128px, Skill/Ulti/Dash88px and Burst100px, leaving more than12px between
outer rings at reference scale. Buttons have20% white fill (35% while pressed),
and the joystick base has18% white fill. Both retain
thin white main borders and a1.5px outer ring at45% white with4px nominal separation.
Inset rings are removed from buttons, joystick base and thumb. Buttons reuse
PNHudGlow's feathered white circular halo. Joystick has no fixed outer ring or
base halo: its direction rim is the sole outer ring and draws the same soft
white feathering along its own outline, including the rounded protrusion.
The rim radius is76px around the140px base, leaving about6px spacing.
It rotates with drag direction. Below5px displacement it stays visible as
a plain circle; from5px to50px pull the rounded protrusion grows linearly0->8px,
with stable angular base width and smooth circle tangents. Glow follows that path.
Changes smaller than.005px of protrusion skip repaint, except exact zero reset.
Release hides the whole joystick. This is visual only; gamepad magnitude/deadzone
and skill input semantics remain unchanged.
It ignores pointer picking and does not expand the touch area.
The cluster fits the safe area using
`UnityEngine.Device.Screen` for both dimensions and safe area in Simulator, and
`UnityEngine.Screen` in Game/builds, including forced desktop preview. The menu
button and pause card use the same selection. Invalid rectangles fall back to
that screen's full area.

Touching the left half spawns the joystick at the finger position, excluding the
portrait/HP HUD. Its origin stays fixed until release; motion clamps to50px.
The thumb follows Toolkit coordinates; gamepad input negates Y so upward touch
produces positive gamepad Y and downward touch produces negative Y.
Each control captures one distinct pointer. Buttons remain held when dragged
outside and never switch channels. Release/cancel/capture loss resets that control.
Pause, lost focus, application pause, opening the debug panel, dead/missing local
player and UI disposal reset input. Showing the new UI suppresses legacy Mobile
groups with OnScreenControl components; hiding/disposal restores original states.

PNMobileVirtualControl uses Unity OnScreenControl's shared virtual-gamepad
lifecycle: leftStick plus each skill action's first effective non-composite
`<Gamepad>/` binding. Active character binding overrides are respected (Eve Ulti
uses rightShoulder); transport rebind releases the old held value first. Actions
without a suitable gamepad binding cannot receive touch input. Default paths are
used only before a character input action is available. PlayerControls and
PNPlayerSkillControls remain the consumers; UI never executes skills, evaluates
tags, consumes charges or maps a button to attack classification. Skill logic
continues interpreting press/hold/release. No new character/tag mechanism here.
PNPlayerMove no longer creates Jump input (Space/buttonNorth); TryJump remains
available for skill/effect-owned motion.

## Local pause menu

The upper-right hollow four-point sparkle cluster appears on PC and mobile
(56/64 reference-pixel hit regions), inside the Simulator-aware safe area. It has
no background, a thicker white outline and a feathered halo. Escape or gamepad
Start also opens/closes it. The full-screen Toolkit surface uses pn-panel-surface,
soft light, an opaque gradient sphere, tilted astronomical orbits and small stars.
The three near-white rounded buttons have thin white rims/halos, with no enclosing
card background. The choices are Tiếp tục / Cài đặt / Rời phòng; only Tiếp tục works. The other two are
disabled placeholders. There is no title, area name, portrait or extra caption.
Background taps do not close the menu. The debug panel sits below the menu button.

PNPauseMenu uses unscaled UI time: opening settles in approximately1s, with the
core opening first, then staggered orbit/dial expansion and individual choices.
Closing takes .58s: choices leave in reverse order, orbits flatten and collapse,
and the core immediately contracts with a moderate accelerating/easing curve.
The core fades at small size rather than snapping into a zero-size point; the
surface fade overlaps the end and all decorations finish by .56s. Closing during
opening snapshots each part's actual pose/opacity rather than restarting it.
The orbit angles remain fixed when open; stars travel on the two ellipses at26s
and34s per orbit, with different phases. Each has independent front/back layers
and follows its own orbit's opening/closing transform and opacity.
The hollow toggle uses a pause-only plump outline: a wider center, shorter arms,
clearly rounded tips/inner corners and a near-square silhouette. Its two satellite
stars use the same outline. The soft halo follows this shape and the hit region
is unchanged. Filled HUD/mobile/orbital stars keep their demo polygon. The third dial spans240 degrees
with feathered endpoints. Eight decorative stars fade fully out before selecting
a new position within +/-6 percentage points of their authored region centers;
lifetimes and gaps differ. Their random stream is independent of gameplay Random.
Front/back arcs have distinct draw order around the sphere. The orbital star is
a complete sibling above all decorations when in front, below the sphere when
behind, and below all menu Buttons in both cases; it inherits no orbit clipping.
Combat HUD/world panels and mobile
controls hide while it is open, including during the closing animation.

Offline sessions set global time to zero immediately on opening and restore the
previous time scale after closing, rather than force 1. Host/Client sessions keep
the simulation running. Only player entities owned by LocalOwnerId have movement
and skill input gated. Running casts retain their existing clocks/behavior;
Ability is not disabled/reset. Online menu grants no invulnerability or room-wide
pause. Global TimeStop remains separate future work, followed by Buff.

MenuInputBlocked is independent of each component's gameplay CanInput setter:
gameplay locks/unlocks acquired while the menu is open survive closing. Skill
input cancellation releases hold/repeat, and touch values reset immediately.
After closing, held gameplay controls must return to neutral before rearming.
UI disable, session replacement/unload and destruction release menu-owned gates,
time and input subscription. The virtual mobile device stays alive under the menu
so legacy controls remain suppressed. Live transport rebind disables each
OnScreenControl before changing its path, then restores its enabled state; this
avoids double insertion into the Input System's linked list during owner changes.

PC mouse uses the existing InputSystem UI Point/Click bindings. Menu and Continue
are normal Toolkit Buttons and work independently of gameplay's input gate and
global time scale; Settings/Leave are deliberately disabled. The focused
PNPausePlayReview.RunMouse check sends native Mouse position/leftButton state,
not direct UI events, and also checks test-panel clicks. Evidence is
.utmp/PauseMouseReview. RunMouse requires desktop mode and preserves the editor
view selection. Select Game through the native Game/Simulator toolbar first;
focusing Game View alone may leave the Simulator platform override active.

PNPauseDrawing uses cached Toolkit Painter2D vector content for arcs, hollow
sparkles and thin button halos. PNPauseArtwork creates two128x128 gradients per
menu instance, destroyed with the menu; there are no new cameras, materials,
scene objects or texture allocations during animation. Decorative elements
ignore picking. Translation/scale/opacity drive animation, not per-frame resizing.

Run PNPausePlayReview.Run in unpaused Ready Play Mode for disposable native
movement/input checks and real screenshots in .utmp/PauseCelestialReview. Its Host/Client
policy checks temporarily simulate session roles and do not test a network
transport. It restores input settings/device filters, UI settings, session role
and time, and unloads its fixture scene. Do not save/reload the authored scene.

PNPauseVisualReview.Run reviews the selected Game/Simulator view in Ready Play
Mode: actual safe-area bounds, front/back orbital star layers, interrupted opening,
closing cleanup and rendered frames. Captures read the selected
PlayModeView render target and normalize graphics-backend row orientation.
Evidence is .utmp/PauseCelestialReview (GameVisualResults.txt,
SimulatorVisualResults.txt and Game*/Simulator*.png). These Editor checks do not
measure physical phone GPU performance or verify a Player build.

## HUD and mobile held sparkles

PNHudSparkles retains8x10.4 four-point white glints. PNSparkleGlyph shares the demo
polygon proportions across filled HUD, mobile and pause stars: white vector core
with lightly rounded corners and a separate colored Gaussian-blur mask. Only the
hollow pause-toggle cluster has the wider, more rounded pause outline.
No stacked thick strokes or pointed miter joins simulate the glow. Mobile glints
compensate inherited cluster scaling to preserve a6 reference-pixel peak width;
the animation still shrinks/fades naturally. Each lives .65–1.15s with demo
alpha/scale keys at0/24/48/100%. Mobile
buttons schedule3 glints per430ms held batch, staggered90ms around the perimeter;
batches overlap rather than limiting the effect to three visible stars. Release
cancels pending glints and lets active ones fade. Pause, focus loss, cancel/reset,
hidden controls, player changes and disposal clear or retire owned effects.

Local HUD compares real Current HP before/after refresh. It updates HP immediately,
then schedules7 avatar glints over600ms and10 health-bar glints over650ms. Glow is
green for HP increases; HP loss interpolates the demo's red hue by remaining HP.
Each new HP change replaces the previous burst, preventing mixed heal/damage
colors during rapid changes. Initial/entity-switch snapshots and max-only changes
do not burst. Menu-hidden changes update the baseline without delayed replay.
This is presentation of net observed HP change, not a new gameplay heal/damage tag
or a guarantee to distinguish multiple opposing commits inside one UI frame.

Fixed pools:16 per mobile button,28 avatar,40 health. No GameObjects, cameras,
materials, coroutine timers or per-glint allocations; transform/opacity update
active glints without rebuilding their core geometry. White core remains white;
only the halo changes color. Decorative layers ignore picking and sit outside
the portrait/HP clipping elements. Two128x160 blur masks are shared through
ref-counted UI-owned PNSparkleArtwork leases; the last document releases them.
Pools retire with their owning UI documents.
PNSparklePlayReview.Run uses a disposable registered player and native Touchscreen
events, real SetCurrent/Heal commits, screenshots and lifecycle checks. Evidence:
.utmp/StarShapeReview. Older .utmp/SparkleReview results are history.
Physical mobile GPU/ergonomics remain unverified.

## Content contracts

Ability JSON now requires a boolean `showInUI` on every definition, even hidden
ones. Inspector/code settings expose the same boolean (false means hidden).
Optional `displayOrder` defaults to integer zero; ties sort by Ability ID.
Optional `iconPath` defaults to empty; `EveCharacterDefinition.PortraitPath` and
character JSON `portraitPath` likewise default to empty. Registry holds strings,
never Sprite references. Resources paths omit `Resources/` and extensions, e.g.
`UI/Icons/Dash`; empty uses placeholders. Malformed paths fail content validation.
Missing/wrong Sprite loads warn once per path/session and use placeholders.
UI owns its Sprite reference cache and clears it on disable/session replacement.

## Camera, ownership and lifecycle

`LocalOwnerId` defaults to offline owner 0. Co-op bootstrap must assign the local
peer's owner ID; optional `LocalPlayerId` disambiguates multiple owned players.
Remote players cannot become the HUD player through a tag lookup. `ViewCamera`
defaults to `PNViewCamera.Current` (the camera director's output in a play session,
`Camera.main` otherwise) and must be a URP Base camera. In a session the overlay camera is
synchronised from `PNViewCamera.Updated`, i.e. after Cinemachine's Brain wrote the pose;
syncing earlier copies the previous frame (a measured one-frame lag, now 0 of 90 frames).
The transition screen (`PNUiToolkitTransitionScreen`) uses its own PanelSettings above the HUD
(sorting 150).

World documents face the camera and follow the projected Sprite quad: horizontal
anchor is the midpoint of its screen-space left/right edges, vertical anchor is
its top plus6 reference pixels. This avoids entity-root/Sprite depth offsets
shifting labels sideways under perspective near screen edges. Sprite-less
entities retain configured offset anchors. Their world scale
compensates camera depth/zoom to match HUD reference pixels. They render in an owned
URP overlay camera with the same renderer and cleared depth, so terrain cannot
hide them. Runtime reserves layer31 by default, masks it out of the Base camera,
and restores that bit/removes its camera on disable, camera replacement or destroy.
Keep this layer exclusive to combat UI. Native UI's interaction colliders are
trigger/disabled, and world/HUD decoration ignores pointer picking.

Dead, disabled and offscreen entities hide world panels; unload retires them.
World panels also hide while the rebuilt HUD's resolved height is NaN, infinite
or nonpositive. They retry on subsequent refreshes once Toolkit layout is ready;
the same valid reference height drives both screen-anchor spacing and world scale.
Counters/timers remain owned by Ability. Size, columns, scale, offsets and layer
are Inspector settings on the runtime component. Ability icons remain35px with
10px horizontal separation and4px vertical separation; grid/world-document width
accounts for horizontal spacing. Crowded panels may still overlap.
Registered Buff UI is implemented in RulesVersion61; Rogue Buff UI remains separate.
Final Buff artwork and automatic overlap avoidance are deferred.

## Ability presentation effects

World Ability cards retain element identity across state/value changes. Becoming
available, a new active State instance or recovery completion triggers one0.2s
white halo pulse and icon bounce (35->38->35px at default size). The halo uses25%
of its original effect intensity, with.04s rise/.16s soft fall. Initial snapshots
do not pulse. Increased stack/charge values bounce the number badge5%, rising
over.06s and returning over.18s, without number glow. Values always update
immediately, but pulses start at most once per.3s and never restart an ongoing
pulse. Badge transform origins stay at the icon-facing edge (right for charges
on the left, left for counters on the right). Countdown ticks/decreases do not animate.
Inactive IDs with no real cooldown fade for0.1s, then retire. Remaining cards
compact left-to-right/bottom-up with0.2s smooth translation. Newly visible cards
fade in over.12s on the same appearance timeline as the icon pulse. Retargeting
uses the current interpolated position; bottom-relative coordinates keep the
lowest row anchored when the panel gains/loses rows. Returning during fade
cancels retirement and resumes opacity from its current value.
UI effects use Unity unscaled time; they are decorative
and never mutate Ability, stats or skill input. Disable/session unload destroys
their owning documents; there are no animation coroutines/subscriptions/resources.
Cooldown numbers and existing available/inactive opacity are unchanged; no
cooldown progress overlay is added.

In unpaused Ready Play Mode, `PNAbilityUIPlayReview.Run()` loads a disposable
player fixture with State/Counter/Cooldown definitions. It checks startup/no-loop
behavior, retained elements, ready/number pulses, fade and reflow, captures real
frames, unloads the fixture and removes its editor frame pump. Evidence is
`.utmp/CombatUIReview/AbilityEffectsResults.txt` and `HudMinimal/AbilityEffects-*.png`.

## Verification

Run `Tests/EveEngine/EveRunSmokeTests.ps1` for strict data/read model contracts.
In Ready Play Mode invoke `PNUIPlayReview.Run()` to create disposable three-player
fixtures. It captures spaced/crowded views and debug selections, checks ownership,
visibility and cleanup, then destroys/unloads fixtures and restores UI settings.
Evidence is in `.utmp/CombatUIReview`. No authored scene is saved by this helper.

`PNMobilePlayReview.Run()` in Ready Play Mode uses a temporary InputSettings clone
and synthetic Touchscreen/Keyboard devices. Checks run in the player loop so they
read the same input state as gameplay. It exercises all four analog directions,
outer-ring separation, multi-touch/real movement,
held-button drag, pause/focus/cancel reset, original five input channels, absence
of Jump input and virtual-device disable/rebind. Settings/devices are cleaned up;
results are MobileResults.txt and HudMinimal/MobileControls.png. Physical phone
touch ergonomics and a full device/resolution sweep still need device testing.
