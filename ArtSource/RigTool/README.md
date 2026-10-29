# Rig tool (authoring tool, not shipped with the game)

EN: A browser tool to edit a Visual rig and its motions and to export content JSON in exactly the schema the game reads (`extensions.project_n.visualRigs / visualParts / visualMotions / ...`, see `Docs/VisualPlan.md` 3.3 and `Assets/Core/Visual/PNVisualREADME.md`).
VI: Công cụ trình duyệt để sửa rig và chuyển động rồi xuất JSON đúng schema game đọc.

Open `rig_tool.html` (offline: the base humanoid JSON and its PNGs are embedded). `python build.py` rebuilds it from `rig_tool.template.html` and the files in `Assets/StreamingAssets/BaseContent/visual`.

## What it does

- **Open JSON / Add PNG:** load any content document (with `extensions.project_n`) and the PNGs it names (matched by file name, kept under the path the JSON uses).
- **View:** pick a rig, a build (short / normal / tall) and a motion; scrub or play the time; mirror to face left; show bones and sockets.
- **Click a part** to select its bone. Drag the sliders (rot, x, y, sx, sy) to pose it, then **Đặt khóa tại t** writes the value as a key at the current time on that bone's track (a track is created when missing; a `wave` track is edited in the JSON box).
- **Motions:** new / duplicate / delete, duration, clock (`time` / `phase`), loop, blendIn, channel (`base` / `overlay` + mask), keys table.
- **Rig tables:** bone rest offset, scale and scale group; slot layers; socket positions. Edits apply at once.
- **Validation** mirrors the C# parser limits (ids, ranges, key counts and order, one root, no cycles, image paths, overlay mask, ...). Export is disabled while errors remain.
- **Export JSON** downloads `visual_content.json`; the box at the bottom shows the same text.

## Verified

The JS evaluator gives the same walk thigh angle as the C# one (27.93 degrees at t = 0.3), a motion made in the tool (`spin`) loads in the real parser (`PNBuffContentBatch`) and `PNVisualAnimator.Sample` returns 45 at t = 0.25 on its 0..90 keys.

## Limits (not done)

- Blending, overlays on top of a base, expressions/face variants, blink and the build-aware preview of sockets are not previewed; the tool shows one motion at a time.
- It edits the first appearance only for colours and the part of each slot; there is no UI yet to create rigs, parts or appearances from scratch (edit the JSON box, or open a JSON file) and no flipbook editor.
- Part images are drawn at 1 : 1 from the PNG; the game's atlas, mesh and shader are not involved.
