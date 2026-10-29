# Visual (rig puppet), RulesVersion 103

EN: Players and humanoid enemies are drawn as a **rig puppet**: a skeleton of parts (head, chest, hips, thighs, shins, face) driven by data. Everything is JSON + PNG interpreted by code at run time: no Animator, no AnimationClip, no bone GameObjects, no atlas baked in the Editor, so a mod ships only JSON and PNG. Design record and decisions: `Docs/VisualPlan.md`. Authoring tools: `ArtSource/VisualDemo` (the humanoid demo and the PNG/SVG pipeline) and `ArtSource/RigTool` (general editor, exports the JSON below).

VI: Người chơi và quái hình người được vẽ như **con rối rig**: bộ xương các part do dữ liệu điều khiển. Toàn bộ là JSON + PNG do code xử lý lúc chạy (không Animator, không clip, không GameObject xương, không atlas bake), mod chỉ cần JSON và PNG.

## Parts of the code

| Type | Where | Role |
|---|---|---|
| `PNVisualRigDefinition`, `PNVisualPartDefinition`, `PNVisualBuildDefinition`, `PNVisualMotionDefinition`, `PNVisualMotionSetDefinition`, `PNVisualExpressionDefinition`, `PNVisualAppearanceDefinition` | Core (`Assets/Core/Visual/PNVisualDefinitions.cs`) | The data. Hard limits are refused, never clamped (`PNVisualLimits`). |
| `PNBuffContentBatch` partial (`PNVisualContentJson.cs`) | Core | JSON of the seven registries inside `extensions.project_n`: `visualRigs, visualParts, visualBuilds, visualMotions, visualMotionSets, visualExpressions, visualAppearances`. Unknown fields refused, references checked (pending batch + registries + pack dependencies). Registry names `visual_*` are in `PNContentModule.Register`. |
| `PNVisualAnimator`, `PNVisualBend` | Core, pure C# | One puppet's pose (and the strip geometry of a bent part): `Play(state)` (base motion, blended from the pose being shown over `blendIn`; an overlay adds on its bone mask), `SetPhase`, `SetExpression`, `Advance(dt)` in entity-clock seconds, `Evaluate()` fills bone/slot affines (metres, y up, mirrored when `FacingLeft`) and socket positions. No allocation per frame. |
| `PNBodySettings.Visual / Build` | Core | `data.body.visual` = appearance id, `data.body.build` = `short|normal|tall` (`project_n:build_<name>`). Absent = the prefab's own sprite, unchanged. |
| `PNVisualPacks`, `PNVisualAtlas`, `PNVisualRuntime`, `PNVisualActor`, `PNVisualSystem` | Game (`Assets/Game/Visual/`) | PNG source per pack folder (`EveGameRuntime.OnContentPublished` publishes the packs), one atlas of every PNG packed when needed, one shared material, one mesh per actor (a quad per part, or `segments` strips for a bendable part; vertex order = draw order), one loop for every actor. |
| `PNVisualBody` | Game | On the entity prefab: `Begin` (factory, after configuring) switches the authored sprite off, creates the actor, applies the build's capsule (feet stay put) and shadow; movement and replicated health drive idle / walk / run / hurt / dead; `Clear` restores everything (pool-safe). |
| Shader `PN/VisualActor` | `Assets/Game/Visual/Resources` | Unlit, pass `Universal2D` (the project's default renderer is the URP 2D Renderer; a pass tagged only `UniversalForward` is not drawn). Part colour and alpha in the vertex colour (sRGB, converted), whole-actor flash and fade in UV1. |
| `PNCameraFacing`, `PNCameraFacingSystem`, `PNCameraFacingTarget` | Core / Game | Turning to the camera is a separate system (Eve's billboard, the Visual actors, props). The actor's object carries a `PNCameraFacingTarget` with its feet as the anchor. |

## Data in short (units: art pixels, y down; `pixelsPerMeter` of the rig converts once)

- **rig**: `id, pixelsPerMeter, facing:"right", capsule{height,radius}, extends?, bones[{id,parent,at,scale,scaleGroup}], slots[{id,bone,layer,face,blink,at}], sockets[{id,bone,at}]`. Exactly one root. A child's offset uses its parent's draw scale (rest scale x build group x track scale), never an accumulated one.
- **part**: `id, image (relative .png, no ".."), pivot, tint (none|skin|eye|hair|dye1|dye2), variants{name: png}, segments (1..16, default 1), axis (y|x, default y)`. **Bendable part**: with `segments` > 1 the image is cut into strips along `axis` and the bone's `bend` / `sway` tracks curve it from the pivot outwards (a fox tail, a cloak, a tentacle): the tangent turns by `bend * s/length + sway * sin(2 pi s/length)` degrees, so `bend` is the total turn of the far end (C shape) and `sway` an S shape; a pivot in the middle bends both halves; the part keeps its length. A `wave` on `bend` and `sway` with different `phase` gives a travelling wag. Children attached to a bent bone do not follow the curve (a tuft at the tip needs its own bone and track). Plain parts are unchanged.
- **build**: `scaleGroups{group:[x,y]}, capsule{height,radius}` (multipliers), `shadow`. Short 0.85/0.9, normal 1/1, tall 1.15/1.25.
- **motion**: `kind pose|flipbook, clock time|phase, duration, loop, blendIn, blendOut, channel base|overlay, mask, noModifiers, speedScale, tracks{bone:{rot,x,y,sx,sy,bend,sway: {keys,ease} or {wave}}}, slotTracks, events, frames, fps`. `bend` and `sway` are degrees within +-360 (additive in an overlay; `sx`/`sy` multiply). `"current"` as the first key starts from the pose being shown (skill strikes; use with `clock:"phase"`). Overlay values are added on the mask.
- **motion set**: `states{name: motion id}`, `modifiers{breathe, blink}`. **expression**: per face slot `variant, sx, sy, ox, oy, rot`. **appearance**: rig, motion set, build, expression, colours (`#RRGGBB[AA]`), slots -> parts, free parts, hide, `blushAlpha`, socket overrides.
- Base content: `StreamingAssets/BaseContent/visual/humanoid.json` + `humanoid/*.png`, written by `ArtSource/VisualDemo/export_content.py`. The test character `project_n:visual_hero` (forbidden, movement only) uses prefab `project_n:visual_test`.

## Add a motion / a part

- Motion: edit it in `ArtSource/RigTool` (or `ArtSource/VisualDemo` for humanoid poses), export the JSON, put it in a content document (or `humanoid.json`), add the state to the motion set. Gameplay calls `actor.Play(state)` and, for skill motions, `actor.SetPhase(0..1)` from the skill's own timeline.
- Part (equipment, hair): add the PNG beside the JSON, a `visualParts` entry, and fill a rig slot or a free part (`bone, at, layer`) in the appearance. A mod does the same inside its own pack folder (the part's id namespace is the pack).

## Not done

The rig tools (`ArtSource/VisualDemo`, `ArtSource/RigTool`) do not show or author `segments`/`bend`/`sway` yet (write them in the JSON); a bent part's children do not follow the curve; Eve and Eve 2 still use their prefab sprite; first enemy art; equipment art and loadout; corpse behaviour; aim UI; shader outline; flipbook art; Android and mobile timing; a Player run of the Visual test character through the real factory in a coop session.
