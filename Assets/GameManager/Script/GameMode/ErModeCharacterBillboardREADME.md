# Character Billboard

## Tieng Viet

`ErModeCharacterBillboard` nam tren child `Visual` cua Eve va Dummy trong ErMode.
Component bu goc nghieng len/xuong cua Main Camera, khong theo yaw/roll camera.
Root gameplay, collider, Rigidbody va child Ground Shadow khong bi thay doi.
Sprite van doi huong bang `flipX`. Chay ca Edit Mode va Play Mode.

`viewCamera` la camera duoc chi dinh; neu de trong thi dung `Camera.main`.
Neu khong co camera thi giu nguyen transform. `footOffset` la khoang cach tu
goc sprite den chan, `footAnchor` la diem chan trong he toa do parent Visual.
Visual xoay quanh diem nay, giu chan co dinh thay vi nhac sprite len khoi san.
Eve dung footOffset 0.14, Dummy 0.275, lay tu bounds sprite hien tai.

Component chuyen dung cho ban thu 2.5D co huong ngang the gioi X; khong phai
billboard 3D xoay theo moi goc camera. Animation doi pivot/ti le sprite can
cau hinh lai footOffset. Khong them Animator hoac doi sprite/material.

## English

Pitch-only billboard on character Visual children, driven by the assigned camera
or Camera.main. Camera yaw/roll are ignored; world X remains the horizontal
sprite axis. Root physics, shadows and flipX are independent. The authored
parent-local foot anchor stays fixed while the visual rotates, including scaled
sprites and rotated parents. Runs in Edit and Play Mode; no camera leaves the
current pose untouched. Reconfigure footOffset if sprite pivot/size changes.

`PNBillboardEditorChecks.Run()` checks both scene attachments and pitch/foot
anchor behavior in a temporary preview scene without saving the gameplay scene.
