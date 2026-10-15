Current Eve uses EveEnergy.shader, procedural moving ribbons, TrailRenderer and
ParticleSystems. EnergyRibbon.png below is the PREVIOUS generated texture; retained
for legacy slash visuals and not sampled by the new Eve effects.

# Eve golden energy texture

`EnergyRibbon.png` was generated with the built-in image_gen tool, inspected and
copied into the project. Genuine alpha; Unity import uses Clamp, mipmaps, 1024 max
size and uncompressed sampling. The slash shader samples the central ribbon strip
and maps it along procedural crescents; no image-editing scripts were used.

Final generation prompt:

> Game VFX production texture, stylized-concept asset for a luminous golden melee
> energy slash in Unity. A single straight horizontal energy ribbon across the
> middle of a square canvas, running left to right, about 20 percent canvas height,
> long fine parallel flowing filaments, ivory-white concentrated thin central core,
> luminous pale gold filaments and warm amber edges breaking into sparse wisps and
> tiny dust at the ribbon ends. Smooth tapered ends, generous completely transparent
> margins on all sides. Flat unlit isolated texture, no curved crescent, no sword,
> no character, no text, no frame, no black background, no checkerboard baked into
> image. This will be UV-mapped onto curved slash ribbons by a shader, so keep it
> horizontal and straight, subtly irregular energy strands with soft alpha falloff,
> clean silhouette.

Five golden HDR ribbon layers and12 tip sparks follow per-hit prefab geometry;
empowered/Repeat/Ulti intensities scale per invocation. Movement has a short golden
TrailRenderer; Burst/activation/Ulti charge have procedural light rings. All are
presentation only. Bloom is enabled in the render QA preview; actual game Bloom
depends on its existing camera/postprocessing configuration.
