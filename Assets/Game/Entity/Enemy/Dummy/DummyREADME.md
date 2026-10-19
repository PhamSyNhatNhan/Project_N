# Dummy

## Tieng Viet

Dummy trong `Assets/Game/Scenes/Test/ErMode/ErMode.unity` dung `DummyStat : PNEnemyStat`,
`EveEntityIdentity`, `EveEntityLoader` va `EveTimeScale`. Loader nap dinh nghia
`project_n:dummy` tu `StreamingAssets/BaseContent/characters/DummyCharacter.json`:
HP 200000000/200000000, ATK 0, DEF 0. Khong co AI, skill hay tu dong hoi HP.

CapsuleCollider va Rigidbody 3D thay physics 2D cu de cung he physics voi Eve.
Gameplay va physics nam tren object goc; SpriteRenderer nam tren child `Visual`.
Child `Dummy Ground Shadow` dung `ErModeCharacterShadow`, theo dummy va chieu
bong len cung ground collider voi Eve. Foot offset lay tu day sprite dummy.
`Visual` dung `ErModeCharacterBillboard` de bu goc camera, giu diem chan co dinh;
bong va collider khong xoay theo. Eve va Dummy dung chung component nay.
Giu nguyen transform, sprite, material, layer Enemy va tag Enemy. Loader tu nap
khi object duoc bat va runtime san sang; khong tu doi trang thai active/gravity.

Stat, TimeScale va StatusEffectManager cu duoc go khoi dummy trong scene nay.
Chua port status effect, iframe hay popup damage. Asset/script cu van giu nguyen
cho noi dung legacy khac. Hitbox va damage cua don slash chua duoc noi.

HP dung float: o 200 trieu, buoc bieu dien khoang 16 HP; damage nho co the bi
lam tron. Gia tri lon duoc giu theo dummy cu, khong doi sang bat tu ngam dinh.

## English

The ErMode dummy now uses Core enemy stats, entity identity/loading and the
engine clock, with a separate JSON definition. Its 3D capsule/body share Eve's
physics world. A `Visual` child owns the original sprite renderer. A sibling
`Dummy Ground Shadow` uses Eve's existing floor-shadow behavior, targeting the
dummy and the same ground collider, with foot offset derived from its sprite.
Original sprite/material, placement, layer, tag and current active/gravity
settings stay unchanged. Enabled objects load automatically in a ready runtime.

No AI, skills, automatic healing, legacy status effects, iframes or damage popup
are added. Legacy assets remain untouched. Slash hit detection is still pending.
At the preserved 200-million HP, float precision rounds small HP changes.

Native integration check: `PNDummyEditorChecks.Run()` outside Play Mode clones
the migrated scene dummy into a temporary preview scene, loads real content,
checks damage/death and reload, and never saves or mutates the gameplay scene.
