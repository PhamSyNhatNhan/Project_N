## Rigid bow floating idle - 2026-11-03 (latest)

Real bow floats1.2cm vertically/0.4cm sideways,tilts2.5deg with offset phases. Independent persistent idleClock;0.15s blend out/in around casts/release/retreat. Rigid geometry unchanged,energy string brightness/flow gentle,rare violet dust only at rest. Shadow actor keeps its existing cast pose,no floating idle. Rules120/mechanics unchanged. Fresh native Bow PASS22 BowIdleReview,main Pierce PASS31 IdlePierceRegression. GIF Starreaper_Bow_Idle.gif1024x768/150frames/5s. No new actual gameplay AI/Player/build/full smoke/performance test.

## Rain grip above forehead - 2026-11-03 (latest)

User chose grip close above forehead. Horizontal offset another12% inward from overhead pose;height/aim/lift path unchanged,Rules120. Fresh Rain native PASS26 RainForeheadGrip,both captures/charge_detail inspected. GIF Starreaper_Rain_Forehead_Grip.gif150frames5s1280x96030fps. No new real AI/Play Mode/Player/build/full smoke/other skill run;explicit visual release fixture limits apply.

## Close overhead Rain bow - 2026-11-03 (latest)

User approved another23% inward move from previous close grip,with higher grip=max(.43,hatTop*.88) near upper mask. Lift-first/reach/rotate sequence retained;up.99+planar.14 visual aim reads about82deg in puppet plane,4deg release dip then recover. Sky shot is presentation,landing/damage/timing unchanged,Rules120. Fresh Rain native PASS26 RainOverheadBow,both renderer captures/charge_detail inspected. GIF Starreaper_Rain_Overhead_Bow.gif1280x960/150frames/5s. No fresh gameplay AI/Play Mode/Player/build/full smoke/standalone/Pierce/Echo checks;preview release remains explicit presentation fixture. Earlier74deg/lower grip descriptions are superseded.

## Rain bow grip brought closer - 2026-11-03 (latest)

User rejected excessive separation: raised Rain grip now38% closer horizontally than previous clearance,slightly lower(max.36,hatTop*.66). Lift begins first,reach follows after0.10s,rotation after0.16s;74deg aim/mechanics/Rules120 unchanged. Fresh native Rain PASS26 RainBowCloser,both renderer captures/charge_detail inspected. GIF Starreaper_Rain_Bow_Closer.gif1280x960/150frames/5s. No fresh actual gameplay AI/Player/build/full smoke/standalone/Pierce/Echo checks;explicit presentation fixture limits still apply. Previous far-clearance description is superseded.

## Speed3.5 / main art+15% / coat0.45 / raised Rain bow - 2026-11-03 (latest)

RulesVersion120. Main registered starreaper_arrow speed/speedMax3.5m/s;new starreaper_echo_arrow retains2m/s and identical physical payload,used only by shadow with echoVisual:true. Real Afterimage shot inherits main3.5. Main ornate flight/stored Pierce art+15%,same volume[0.5,0.3,0.5];rear attaches to string and evaluated centre launch follows larger stored light. Simple clone and Rain art sizes unchanged. Coat fades0.45s after0.16s retreat,total0.61s;clone dissolve0.65s/trail tail0.28s unchanged.

Rain bow translates ahead of hat at upper-mask height before rotation starts0.16s into charge;forward offset accounts for mesh half-width,height for hat top. About74deg aim and string-following nock retained;position/angle recover after shot. No art/rig/shared API changes.

Fresh focused PNStarreaperTests +PNShaderWarmupTests PASS,current asserts3.5 +clone2. Native Pierce31 Speed35Pierce,Rain26 RaisedRainBow,Echo26 Coat045Echo;render frames inspected. GIFs Starreaper_Pierce_35_115.gif3s,Starreaper_Rain_Raised_Bow.gif5s,Starreaper_Coat045_Echo065.gif4s,all1280x96030fps. No fresh real AI/Player/build/full smoke/SDK/shake/performance run;Rain release/retreat explicitly driven previews. Earlier speed2/coat0.22/Rules119 descriptions are history.

## Full retreat coat/trail + clone dissolve0.65s - 2026-11-03 (latest)

User correction: clone now dissolves0.65s;single PNStarreaperEchoVisual.DissolveSeconds feeds shader phase and area retirement. Supersedes0.45 and temporary0.35;logical expiry/shot timing unchanged. Real retreat has full live-mesh/borrowed-atlas moving black/violet coat (private shader echoMode3),original puppet visible beneath. Coat fades0.22s after retreat;trail max0.85m,full puppet bounds height including hat,shortens/fades0.28s after retreat,peels into3 Namu smoke-sequence puffs with sparse stars/specks. No connection to old shadow position. OnDisable/dispose clear owned overlay/material/batches;borrowed actor mesh/atlas untouched. Clone simplified arrows/mechanics unchanged,Rules119.

Fresh native Echo PASS25 RetreatCoatFinal and Pierce PASS31 RetreatCoatPierceRegression,both renderers inspected,shader clean/stopped session/zero leftover coat materials. GIF Starreaper_Retreat_Coat_065.gif1280x960/120frames/4s. Native retreat explicitly animated,not AI/navigation/collision Play Mode proof. No fresh Player/build/full smoke/standalone/Rain/shake/performance test. Old short-trail/0.45s descriptions below are history.

## Short retreat wake + simple echo arrows - 2026-11-03 (latest)

Retreat VFX now stays at the body: max0.22m short dark/violet wake,short dust/one faint gold speck,no line connecting old position. Registered motion unchanged. Shadow alone launches with optional arrow input `echoVisual:true` (strict bool,null/empty defaultfalse). Same projectile physics/damage/pool;Begin resets style each borrow. Simple variant:0.30m two-tip pale violet core,thin black sheath,max0.26m tail,small admitted-hit line;no ornate ribbons/star clusters/gold/spirals. Main Pierce/real Afterimage shot keep original appearance. Volume[0.5,0.3,0.5],speed/timings/damage unchanged,Rules119.

Fresh Echo PASS20 EchoSimpleVerified,main Pierce PASS31 EchoSimplePierceVerified;actual area/two arrows/native renders. GIF Starreaper_Echo_Simple_Arrows.gif1280x960/120frames/4s. Earlier SimpleReview results used old assembly during a corrected local-name compile error,not current proof. No new actual AI/motion collision Play Mode/Player/build/full smoke/standalone/Rain/performance verification. Native retreat movement explicitly animated.

## Tan Anh dark echo presentation - 2026-11-03 (latest)

Successful retreat emits a short black/violet wake with sparse black stars. Shadow now renders its own animated rig silhouette through borrowed mesh/atlas and private dark tint/dissolve material,with an actual rigid bow and drawn energy string. Brighter art stays pale violet;own bow glows before shot. No huge Pierce intake. Fire drives release pose while existing registered projectiles remain unchanged. Logical shadow expiry still after2s +0.15s gap/two shots;standard area retirement keeps visual only0.45s for bottom-up tearing,dust/black stars,late bright-part/bow fade. Cancel/disable/session unload hides immediately;pool release/dispose clear owned subscriptions/materials/meshes. No collider/health,owner-independent. Explicit area clock,PNViewCamera events,no Camera.main. Presentation echo binding is mob-private;shared Visual untouched,RulesVersion119/mechanics unchanged.

Fresh native Echo PASS18 EchoVerified: actual area/two projectiles,delay,retirement,reuse/cancel,owner disable independence,dark bow/atlas,material cleanup;both renderer paths inspected. Pierce PASS31 EchoPierceRegression,Rain PASS25 EchoRainRegression. GIF Starreaper_Afterimage_Echo.gif1280x960/120frames/4s. Source retreat in preview is explicitly moved,not actual AI trajectory/collision/Play Mode proof. No new Player/build/full smoke/standalone/performance run. Early EchoReview/failed EchoFinal are drafts. Echo currently re-created on area Initialize;no performance claim.

## Namu texture-sequence rain explosion - 2026-11-03 (latest)

Replaced procedural black lobes with layered texture sequences: Namu TestSheet row2 frames4..7 impact0.14s,dark shell frames8..15 over0.24s,4 staggered smoke puffs4x3 frames0..10 over0.42s. Own shader20..24 blends frames with organic noise; existing helper uv2.y stores optional frame (default0). Project-owned RainImpactSheet/RainDarkSheet/RainSmokeSheet/RainNoise PNGs derived from pack textures only,no pack code/shader/source changes. Fixed common per-sheet crop removes huge blank margins without per-frame registration jumps. Manifest/hashes/GUIDs in .utmp/StarreaperVfx/RainPackSequences/texture_sources.json. Linear,bilinear,no mipmaps,uncompressed masks,Clamp; noiseRepeat.

Fresh Rain native PASS25 RainPackFinal and Pierce PASS31 RainPackPierceRegression; both URP renderers inspected. GIF Starreaper_Rain_Pack_Sequences.gif1280x960/150frames/5s. No fresh real rain damage/AI Play Mode/Player/shake acceptance/build/full smoke/performance run. RulesVersion119 and mechanics/warning/charge/sky/shake unchanged; native release is an explicit presentation fixture. Earlier procedural-lobe descriptions below are superseded.

## Dense black rain blast + restrained shake - 2026-11-03 (latest)

Impact now punches out5 angular black masses with broken violet edges (private modes18/19), no additive-filled centres. Expansion completes mostly within0.06-0.10s, black bodies fade0.10-0.32s. Pale flash shortened/reduced, dark incoming wake thicker,7 faster/larger fragments, dark dust delayed0.07s. Tail0.42s/mechanics unchanged. RainVisual.Land uses existing camera service:0.025m/28Hz/0.10s Recoil,5m quadratic falloff; one replacing handle per area,disposed on Stop,source at landing. Native preview does not request shake; real gameplay shake remains unverified.

Fresh Rain PASS24 RainDarkImpactFinal, both renderer paths inspected, shader clean, session stopped/no capture cameras. Pierce regression PASS31 DarkBlastPierceRegression after shader branch addition; subsequent edits only touched new18/19. GIF Starreaper_Rain_Dark_Impact.gif1280x960/150frames/5s. No new AI Play Mode/actual rain damage/Player/build/full smoke/standalone/performance verification. RulesVersion119/shared APIs unchanged. Earlier soft-purple explosion description below is superseded.

## Rain contact explosion - 2026-11-03 (latest)

User rejected the3 upright impact ribbons. Replaced them with a short contact flash and low outward black/violet blast lobes, then dispersing dust. Incoming wake continues collapsing into the contact for0.10s; star fragments launch upward and fall under analytic gravity. No vertical impact ribbon/flash. Full tail still0.42s, warning/mechanics unchanged, RulesVersion119. Fresh native Rain PASS23 in RainExplosionBalanced: retained incoming flow,no upright mode13,freeze/cancel/pool/disposal and prior presentation cases; both renderers inspected. GIF Starreaper_Rain_Contact_Explosion.gif1280x960/150frames/5s. No new actual gameplay/damage/AI Play Mode/Player/build/full smoke/standalone/Pierce run. Earlier upright-plume descriptions below are superseded.

## Rain landing presentation - 2026-11-03 (latest)

Replaced old line warnings/falls/burst rings with per-landing retained ink/light mesh batches. Textured grounded violet warning rim maps to actual0.5m radius, dark centre and gold dust; inner light contracts before contact. Last0.2s: an upper tear and accelerating two-tip violet core with dark flowing wake/black stars. At existing landing time: short flash,3 upward irregular violet/black plumes,outward streaks,black star fragments/sparse gold glints,rising depth-spread haze and an ink deposit. No expanding ring; tail vanishes at0.42s within existing area0.45s tail. Timing/damage/targeting/points/dependency unchanged, RulesVersion119.

Area age drives all analytic motion. Camera event redraws same age; Stop unsubscribes/clears without resurrection, reuse retains slots, Dispose releases owned rendering resources. Private shader modes16/17 only,existing shader warm-up unchanged. Native Rain PASS21 in RainLandingAligned (explicit presentation release, not actual damage/AI proof); Pierce regression PASS31 RainLandingPierceRegression. Both renderer paths inspected, final shader clean/session stopped/no capture cameras. GIF `Starreaper_Rain_Landing_Aligned.gif`1280x960/150frames/5s. No fresh gameplay Play Mode/Player/build/full smoke/standalone/performance run. Previous notes that rain landing art remains unchanged/pending are superseded.

## Mua Tinh upward cast presentation - 2026-11-03 (latest)

Rain now has separate phase-clock rain_charge/rain_release poses, lifting/aiming its rigid bow near vertical. Fixed-size stored light follows the pulled string. Three bounded black/violet streams rise from below into its front tip with gold filaments and sparse black stars; tails complete before release. The same registered2s windup/0.5s recovery and area mechanics remain unchanged. Live nocked centre +bow axis feed the area-owned textured upward shot. This visual accelerates upward, leaves a broad dark violet wake, fades and fans into fine violet/gold upper-edge streaks, without a muzzle ring. Final camera events rebuild at the area-supplied age; Stop removes subscription/clears, Dispose owns meshes/materials. Existing rain warning circles, falling lines and burst rings remain pending later visual work.

Fresh native Rain PASS14: real charge Action/cancel and explicit presentation release/sky checks, both URP renderers, freeze/stop/reuse/material disposal. `.utmp/StarreaperVfx/RainPresentationFinal/native.txt`. Pierce regression PASS31 in RainPierceRegression. GIF `Starreaper_Rain_Upward_Cast.gif`1280x960/120frames/4s. No new AI Play Mode/Player/build/full smoke. RainFinal and RainHandoffVerified are failed world-fixture drafts, not validation evidence; real Rain Action-to-area gameplay handoff remains to be checked. RulesVersion119 unchanged, no shared API changes.

## Front-tip absorption, complete tails and black stars — 2026-11-03 (latest)

Current intake targets the moving FRONT tip. Three non-looping streams start at4/14/24% and finish at72/81/90% of the unchanged1.5s windup. Each head arrives first; its tail continues entering instead of disappearing on release. Last tail completes at1.35s; a short linear tip pulse occupies the remaining preparation time. Charge dust also fades before release. Cancel retains immediate cleanup.

Intake/body side ribbons now have black alpha masses and narrow violet edge light, with irregular soft/tapered edges; purple additive fill is reduced. Gold threads retained. Sparse5-point black stars (fine violet rims) accompany active intake plumes and drift as3 small stars in the flight wake. Private shader modes10-15 in the already registered PN/StarreaperPierce, no new textures/pack changes/shared API. Core shape/string follow/centre launch, hitbox[0.5,0.3,0.5], stats/timing and RulesVersion119 remain.

Fresh native PASS31 `.utmp/StarreaperVfx/FrontAbsorptionFinal/native.txt` includes real mesh endpoint verification at the front tip, decreasing last-tail distance and all tails finished before the shot, plus prior movement/physics/cancel/freeze/pool checks. Both renderers and actual frames inspected. GIF `Starreaper_Front_Absorption_Black_Stars.gif`1280x960/90frames/3s. No fresh gameplay AI Play Mode/Player/build/full smoke/standalone run. Previous repeating/mid-core intake descriptions below are superseded.

## Connected sheath and string-following projectile — 2026-11-03 (latest)

Current charge and flight share a connected energy body: a two-tip spindle core (fixed0.34m length,0.048m quad width), soft violet/black sheath and2 unequal animated side ribbons. Pack fan/flow masks are low-opacity detail. Separate head effect removed, contact glints linear. Flight restores a wider soft old-style violet/black wake (up to0.82m) with low-frequency grain. No oval envelope/debug box in normal rendering.3 large staggered intake plumes with gold strips travel into the body, grow in length before width and fade on arrival; body and bow brighten on arrival. Joined strip edges avoid teeth/gaps.

The nocked body translates with the string, rear point attached to its centre, without changing its dimensions. MuzzlePosition is the stored front tip; ProjectilePosition is its centre. The existing selected Pierce BowContext uses the live ProjectilePosition so release does not jump forward half a body. RulesVersion119 for this evaluated launch-origin change. Physical volume[0.5,0.3,0.5] and all skill damage/timing/speed values unchanged. Other skills' cast logic, rig/cloth/strings and pack files remain unchanged; the shared afterimage projectile inherits new flight art.

Fresh native PASS28 `.utmp/StarreaperVfx/ConnectedEnergyVerified/native.txt` includes backward motion, rear/string attachment, fixed dimensions and actual centre launch, plus prior actual hit/miss/pierce/freeze/cancel/pool cleanup. Both renderers +charge close-up inspected. GIF `Starreaper_Connected_Energy_And_Draw.gif`1280x960/90 frames/3s. No new gameplay AI Play Mode/Player/build/full smoke/standalone run. Older disconnected-head/thin-trail notes below are superseded.

## Layered spindle light with two pointed ends — 2026-11-03 (latest)

User-authorized Namu/Hovl texture revision. The bright core is a symmetric spindle: pointed at both ends, thicker middle, fixed0.34m length /0.060m quad width while charging and flying. Three byte-identical texture copies have independent GUIDs in Art/Resources/StarreaperVfx: BeamTrail.tga (Namu Trail), FlowTrail.tga (Namu Trail05) and CoverTrail.png (Hovl Trail48). Original pack content unchanged; no pack code/shader copied. Linear bilinear uncompressed masks; flow repeatsU, other axes clamp, no mipmaps.

Existing2-batch private shader adds texture-driven core, violet/black flowing wake, backwards-fanning cover and short leading streaks. No solid head, round head glow or oval envelope. Charge packets advance inward and vanish, carrying gold filaments; they are not persistent complete arcs. Bow glow, puppet poses and2 strings retained. Hitbox[0.5,0.3,0.5],1.5s charge and RulesVersion118 unchanged. The afterimage shares the projectile art, other skill VFX unchanged.

Fresh native PASS26 `.utmp/StarreaperVfx/LayeredTrailFinal/native.txt`, both renderers +charge close-up inspected; includes actual hit/miss/pierce/cancel/freeze/pool cleanup and texture-layer checks. Final GIF `Starreaper_Layered_Two_Tip_Stream.gif` (1280x960,90 frames/3s); close-up `LayeredTrailFinal/charge_core_detail.png`; source hashes/GUIDs in texture_sources.txt beside it. No fresh gameplay Play Mode/Player/build/full smoke or standalone suite run. Older thin-core/solid-head descriptions below are history.

## Projectile height0.3m — 2026-11-03 (latest)

User selected full volume[0.5,0.3,0.5] (sideways,height,forward). RulesVersion118. Both Pierce and the shared afterimage projectile use this size. Oval envelope remains removed; wire is preview-only. Fresh native PASS23 in `.utmp/StarreaperVfx/HitboxHeight03/native.txt` includes high-target rejection and retained sideways-edge hit after lowering the target; focused PNStarreaperTests +PNShaderWarmupTests PASS. No gameplay Play Mode/build/full smoke rerun. Previous1m height notes below are historical.

## Actual hitbox review, oval envelope removed — 2026-11-03 (latest)

The oval/ring envelope is removed from the live projectile. Its full registered box still measures0.5m sideways x1m high x0.5m forward; no size reduction has been chosen. The native review accepts an optional drawHitbox=true to render12 green OBB edges using the actual live Position/Forward/HalfExtents. This is an Editor preview only, cleaned on exit, not a shipping effect. Fresh native PASS22 at `.utmp/StarreaperVfx/ActualHitboxOutline/native.txt`; GIF `Starreaper_Actual_Hitbox_Outline.gif` there. Head glow/Trail48/Ori styling and stronger Starweaver-like intake remain discussion pending implementation authorization. RulesVersion117 unchanged; no new gameplay Play Mode/Player/full smoke/build.

## Light stream, charge animation and crown/tail strings — 2026-11-03 (latest)

Current visual is a thin light stream, not a solid arrow or head. Its stored core stays0.34m long with0.014m quad width throughout charge; brightness/flow increase. Four curved violet/black intake ribbons carry fading gold packets into it, and the whole bow glows. Flying energy uses the actual registered HalfExtents for a broad soft translucent envelope around the thin core. Registered volume full size is now[0.5,1.0,0.5] (twice the old width/height, unchanged forward length); other timing/damage/speed values remain unchanged. The shared afterimage projectile inherits the appearance and volume.

Pierce uses phase-driven pierce_charge / pierce_release poses for its1.5s windup and existing0.4s recovery. New motion states and2 standard puppet strings are present in both game and editor work document: hat_crown on hood; hat_tail_tip on dynamic hat_tail_c. Hat/coat springs/skin/PNG art and other skills' shoot motion retained. Authored through editor Session (`.utmp/StarreaperVfx/LightRig/author.mjs`); before copies preserved there.

Fresh native PASS22 `.utmp/StarreaperVfx/LightStreamVerified/native.txt` includes an actual hit outside both old width/height bounds, fixed core dimensions, poses,2 strings, freeze/cancel/pool cleanup. Bow regression PASS20; focused PNStarreaperTests + PNShaderWarmupTests PASS. Both renderers captured, final GIF `.utmp/StarreaperVfx/Starreaper_Light_Stream_And_Strings.gif` (1280x960,4s). RulesVersion117. No fresh gameplay Play Mode/Player/build/full smoke. The soft envelope is sized from the physical box and has a fading oval appearance; it does not change the framework's box contact geometry. Historical solid-head notes below are superseded.

## Xuyên Tinh VFX + 1.5 s windup — 2026-11-03 (latest)

- Registered windup is now1.5 s (was1.0);250% ATK magic/crit,8 s cooldown,3 m trigger range,2 m/s projectile and5 s life remain unchanged. RulesVersion116 for the changed registered content/new shader registration.
- Pierce-only charge gathers violet/black flow and sparse dust toward the tip, compresses before release, then flashes sharply. Cancelling clears charge without a shot. The bow remains rigid. Its drawn arrow uses the same solid, four-faced violet starlance head as the flying projectile; the old flat white arrow icon is no longer rendered. The live BowContext supplies the actual charged tip as launch Position through the selected registered Action/Operation, following the existing Starweaver pattern.
- PNStarreaperArrowVisual owns solid head + two reused alpha/additive mesh batches: dark flowing wake, thin luminous shaft, violet dust and short linear hit glints. Only hits admitted by base directional rules create a contact effect; the arrow continues piercing and each target is still hit once. Contact positions stay in world space; age comes from the projectile manager, camera alignment from PNViewCamera.Updated. Begin/End reset particles and subscriptions; DisposePooledResources releases owned meshes/materials. Tàn Ảnh uses this same projectile, so it shares the new flying arrow appearance; its cast and timing were not changed. Mưa Tinh visuals unchanged.
- New private Resources shader PN/StarreaperPierce has both URP passes, registered warm-up alias base:shader_starreaperpierce (21 shader entries). No pack code/shader copied; existing project noise texture reused. No shared projectile API/lifecycle change.
- Fresh native PNVisualEditorChecks.RunStarreaperPierce PASS14:1.5 s timing, live muzzle, two actual damage hits/continued flight/no repeat, zero-clock charge/flight, cancel, pool reset/reuse and owned-material cleanup; actual rendered2D/3D charge and flight images in `.utmp/StarreaperVfx/PierceRelease/`. Bow regression PASS20 in PierceBowRegression/. Focused standalone PNStarreaperTests + PNShaderWarmupTests PASS. No new real AI Play Mode/Player/build/full smoke/GPU warm-up Pump run. GIF `.utmp/StarreaperVfx/Starreaper_Pierce_1_5s.gif`:1024x768,120 frames/4 s at30 fps.

## Integrated lower limb — 2026-11-03 (latest)

The lower limb and grip now form one uninterrupted section loft, including their front/back ridge and perimeter. There is no shaft crossing or terminating within a separate lower armour piece. The lower silhouette flares from the frame and tapers to the string tip. Upper mount transitions gradually beneath the crescent and separate shoulder plates. This supersedes the earlier separate lower shell. Fresh native PASS20: `.utmp/StarreaperVfx/JoinedLimbsRelease/native.txt`; actual preview frames inspected; gameplay Play Mode not rerun.

## Continuous angular bow frame — 2026-11-03 (latest)

The current user decision supersedes the round grip below: no tube or rounded sleeve. A single hard-edged frame connects the upper crescent and lower pointed armour; the lower frame end is covered. Both ends of the lower armour taper to points. Energy string and rigid-body aiming/draw behavior are unchanged. Native preview PASS20: `.utmp/StarreaperVfx/AngularFrameFinal/native.txt`; no new gameplay Play Mode/build.

## Physical weapon volumes, no outline — 2026-11-03 (latest)

The old flat traced/outlined construction is replaced: circular structural tube and thicker round grip, separate raised armour shells on the upper frame, wedge-loft crescent with a real spine and tapering edges. Black contour renderers/painted face overlays removed. D silhouette, rigid body, energy string and existing muzzle/aim contracts retained. Cloth and gameplay unchanged.

Private PN/StarreaperWeapon material uses actual normals, opaque depth and soft specular; both URP renderers supported. Registered in existing warm-up list (20 entries with aliases/visual variants as tested). Native PASS20 `.utmp/StarreaperVfx/SolidVolumesRelease/native.txt`; focused standalone PNShaderWarmupTests PASS in SolidWeaponFocused/. GIF Starreaper_Solid_Volume_Bow.gif and extra oblique geometry photo volume_oblique3D.png (character/string hidden only for the detail photo). No fresh gameplay Play Mode/Player/build or shared warm-up Pump run. Earlier flat-construction notes below are historical.

## User-selected D bow — 2026-11-03 (latest)

Rebuilt the bow from the approved concept D silhouette: extruded concave upper crescent with deliberate angular facets, layered shoulder, curved narrow shaft and compact lower limb. Thin dark contours preserve readability. Main anchor span uses existing bowHeight0.34 m, total silhouette about0.54 m with its upper ornament. Inner shoulder anchors the string, not the ornamental tip. Separate bright core/soft violet halo draws the energy string. Body stays rigid through draw/release; only string/nocked arrow deform. Aim/mirroring/muzzle-follow remain; cloth rigs and gameplay values unchanged.

Native PASS16 `.utmp/StarreaperVfx/ConceptDRelease/native.txt`; GIF Starreaper_Concept_D_Bow.gif in the same root (3 s/30 fps), selected source sheet Concept_D_Selected_Sheet.png retained for comparison. Actual disposable preview, not a fresh gameplay Play Mode/Player run. Old bow designs below are historical.

## Rigid asymmetric upper-scythe bow — 2026-11-03 (latest)

Current bow is rigid and asymmetric: long upper scythe crescent extends backward above the hood, lower limb is short/plain, narrow grip. Only string/nocked-arrow pull changes during draw/release; no limb bend, body recoil, hover bob or draw translation. Aim rotation is retained. Left mirroring uses X scale to keep the upper crescent above. String anchors follow the unequal tips and the centre draws0.09 m back. Geometry face ordering avoids cross-section stripes with the existing shader. No skill/projectile/stat changes; soft hat/coat rigs retained.

Native bow review PASS14 `.utmp/StarreaperVfx/AsymmetricFinal/native.txt`; GIF Starreaper_Asymmetric_Scythe_Bow.gif there (3 s/30 fps). Actual disposable preview, not fresh gameplay Play Mode/Player evidence. Prior symmetric/bending bow descriptions below are history.

## Soft hat tail + two coat flaps — 2026-11-03

Three independent3-bone spring chains now deform the hat tail and left/right coat flaps. Hat front and coat are16x16 explicit skinned grids; weights keep the face opening/main hat and coat centre fixed. Existing PNGs/pivots/layers and hat back unchanged. Small phase-offset idle waves + existing movement inertia, tighter roots/looser tips; coat sway is smaller than hat tail. No gameplay/bow/stat/shared runtime changes. Game JSON and editor work document updated through Session.edit/ops; before copies in `.utmp/StarreaperRig/`.

Authoring checks PASS7 (rest skin identity/weights/anchors/content). Native PASS18 (13 bow +5 cloth), `.utmp/StarreaperRig/ClothFinal/native.txt`; GIF `Starreaper_Hat_And_Coat_Springs.gif` in the same root (6 s/30 fps: idle, run, rapid retreat and settle). Native disposable render fixture only; no fresh gameplay Play Mode/Player/build evidence. Older notes below are history.

## Twin-sickle bow revision — 2026-11-03

Latest bow art replaces the flat additive ribbon: solid violet beveled limbs with backward hooks, dark grip, thin violet energy edge/string, actual tension-driven limb deformation and a short release rebound. It remains code-built on existing project shaders; no pack code/new shader. Draw pose moved forward to avoid the face; MuzzlePosition still follows the existing bow transform contract. Skill numbers/projectile/rain/shadow unchanged.

Native `PNVisualEditorChecks.RunStarreaperBow(output,movieFrames)` PASS13, `.utmp/StarreaperVfx/BowFinal/native.txt`; actual30 fps/3 s GIF `Starreaper_Sickle_Bow.gif` in the same root. Preview rendering only, not a fresh gameplay Play Mode/Player run. Final bow design still awaits user judgement. Earlier descriptions below may describe the original ribbon.

# Starreaper

EN: a normal ranged mob that fights with a violet bow (skills 2026-11-03, `Docs/StarreaperSkillsPlan.md`, `Docs/StarEnemyRosterDraft.md`). Content: `StreamingAssets/BaseContent/characters/StarreaperCharacter.json` (`project_n:starreaper`, `body.prefab` `starreaper`, `body.visual` `project_n:starreaper`, run speed 0.9, other stats estimated to be balanced later) and the rig-editor document `visual/starreaper.json`. Prefab `Prefab/Starreaper.prefab`, catalog key `project_n:starreaper`; components `EveEntityIdentity`, `EveEntityLoader`, `EveTimeScale`, `PNStarreaperStat`, `PNAbilityManager`, `PNBuffManager`, `PNEnemyMove`, `PNStarreaperSkill`, `PNStarreaperBehavior`, `PNStarreaperPresentation`, `PNVisualBody`. ErMode has `Starreaper Spawn`.

Skills (all registered in `PNStarreaperCombatContent`, tag `base:magic`, damage only on the layers of the caster's `TargetLayers`; stun cancels a cast while it is aiming or gathering, being hit does not):
- **Xuyên Tinh** `starreaper_pierce` (8 s, attack, 250%): aims 1.5 s, then one arrow `starreaper_arrow` (speed 2, lives 5 s, ignores walls, never ends on a hit, each target once). The shot re-aims at the best target (`create_projectile aimRadius`). Starts when the target is within 3 m.
- **Tàn Ảnh** `starreaper_afterimage` (20 s, attack): starts within 1.5 m and only with room behind (walls and floor checked by the behavior and again by the trajectory). The shadow area appears when the step back starts; the body steps back 1.5 m in 0.16 s (straight CurveXZ), then shoots the 250% arrow. The shadow (`PNStarreaperShadowArea`, no health, independent of the owner, snapshot of the owner, at most one) shoots two arrows 2 s after it appeared: 120% each, 0.15 s apart, the second `side * 10` degrees off the first; the side alternates between uses (state on `PNStarreaperSkill`). It is the Starreaper's rig puppet under a violet overlay (the shader mode of the Starchanter's puppet light).
- **Mưa Tinh** `starreaper_rain` (12 s, skill): gathers 2 s, shoots one arrow into the sky; `PNStarreaperRainArea` (dependent: the owner's death ends it) warns every landing zone at once, the first arrow lands after 1 s, then one every 0.15 s; 5-7 arrows within 2 m of the target at the shot, at least 0.8 m apart (`PNStarreaperPlanner.RainPoints`); a landing explodes in radius 0.5 m for 150% (overlapping explosions count separately; the falling arrow itself deals nothing). Starts when the target is within 2.5 m.

Behavior (`PNStarreaperBehavior`): Tàn Ảnh, else Mưa Tinh, else Xuyên Tinh, else walk up to 2.5 m and stand. Ranges are the registered `range` of each skill. Presentation: `PNStarreaperPresentation` builds the bow from code (ribbon, string, nocked arrow) on the star effect shader; arrow texture `Art/Resources/StarreaperVfx/Arrow.png` is a byte-identical copy of the pack's `ARPG Effects/Textures/arrow.png` (no pack code or shader is used).

Checks: smoke `PNStarreaperTests` (registration, schemas, planner); Player review `PNStarreaperPlayReview` (`-pn-menu-play-starreaper`, `.utmp/Camera/RunStarreaperFlow.ps1`); `PNStarMobChecks.Run()` (content, prefab, catalog). Not done: detailed shadow effect, final bow shape, elite mechanism, coop/mobile checks.

VI: quái thường đánh xa dùng cung tím với ba chiêu (Xuyên Tinh, Tàn Ảnh, Mưa Tinh); số liệu nằm trong payload đăng ký ở `PNStarreaperCombatContent`, hành vi ở `PNStarreaperBehavior`, hiệu ứng cung/mũi tên dựng bằng code. Bóng (Tàn Ảnh) và mưa là Area do manager sở hữu nên sống độc lập với cast.

Projectile readability trial (2026-11-03): Normal Pierce core thickness x1.25, cover/wake widths x1.35. Nocked Pierce matches; Rain and simplified echo unchanged. Physics, launch, speed and damage unchanged. Fresh native checks and camera-scale comparison: `.utmp/ProjectileReadability/`. Native preview only; no new AI/Player evidence.

Current speed update (2026-11-03, Rules129): main starreaper_arrow speed/speedMax4m/s (was3.5); shared by Pierce and real Afterimage shot. Clone echo arrow stays2m/s. Native Pierce PASS31 at `.utmp/ProjectileReadability/ReaperSpeed4/native.txt`; standalone expectation updated,not newly run. Other projectile parameters and art unchanged.

Pierce purple-sheath tuning (2026-11-03): surrounding violet quads/ribbon light widths x1.30 while nocked and flying. Core, dark ink, wake/trail, colours/brightness, physics and4m/s unchanged. Mob-local Body optional sheath multiplier defaults1;Rain/sky/fall and echo unchanged. Native PASS31 and direct geometry ratio check; `.utmp/ProjectileReadability/PurpleSheath30/`.
