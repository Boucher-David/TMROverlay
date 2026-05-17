# Livery Editor Research

> Research note: this is not current V1 product scope. It is preserved as
> evidence for a possible future livery-editor/product branch and should not be
> read as an active overlay or settings task list.

This note records the first local inspection of the uploaded iRacing livery template and what it implies for a future TmrOverlay livery editor. It is research only; no production livery feature is currently planned for the current core-overlay milestone.

## Local PSD Under Review

File:

```text
livery-poc/BMW M4 GT3.psd
```

Local inspection results:

- Photoshop file signature: `8BPS`
- PSD version: `1`
- Canvas: `2048 x 2048`
- Color: RGB, 8-bit, 3 document channels
- File size: about 37 MB on disk
- Embedded profile: `sRGB IEC61966-2.1`
- XMP creator history includes Photoshop CC 2019 on Windows and a later Photoshop 26.10 save.
- Layer records: 57
- Every layer record has a transparency channel plus red, green, and blue channel data.
- Pixel/channel data is mostly RLE-compressed.

## Inferred Layer Structure

Reading Photoshop section-divider records bottom-to-top gives this practical hierarchy:

```text
[Group closed] Custom Spec (hidden)
  [Group open] metal
    - metallic
    - BG
  [Group open] rough
    - Carbon FIber
    - Layer 3
    - Layer 2
    - BG
  [Group closed] Blue Channel Clearcoat
    - Base Paint

[Group open] Turn Off Before Exporting TGA
  - wire (visible locally after metadata edit)
  - Mask
  - License (hidden)
  - Sponsor (hidden)
  - Number Blocks (hidden)
  - Car Mandatory

[Group closed] Paintable Area
  - IMSA Car_decal (hidden)
  - Car_decal
  - Carbon Fiber
  - pitbox colors (hidden)
  [Group closed] Car Patterns (hidden)
    - car_pattern_001.tga
    - car_pattern_001_spec.tga
    - car_pattern_002.tga through car_pattern_024.tga
  - Paintable Area
```

This looks like a normal iRacing paint template:

- `Paintable Area` is where the driver livery content belongs.
- `Turn Off Before Exporting TGA` contains guide/stamp/reference layers that should not usually be baked into the final car texture.
- `Custom Spec` is a PBR/spec-map authoring area for metallic, roughness, clear-coat, and lighting-effect behavior.
- The template contains built-in pattern layers, but their exact visual content has not been decoded into preview images yet.

## iRacing Paint Runtime Expectations

iRacing's custom paint flow expects an external editor that can open layered PSD templates. iRacing recommends downloading the car paint template from the iRacing UI, then saving a flattened custom paint TGA to the matching local paint folder. These notes were checked against iRacing support pages on 2026-05-06.

For the normal car paint:

```text
Documents\iRacing\paint\bmwm4gt3\car_<CustomerID>.tga
```

The BMW M4 GT3 active car path is currently listed by iRacing as:

```text
\bmwm4gt3
```

That means the local Windows paint folder should be:

```text
Documents\iRacing\paint\bmwm4gt3\
```

iRacing documents the normal custom-paint texture as a `24 bit TGA with RLE compression enabled`. The template here is already `2048 x 2048`, which is one of the supported custom paint texture sizes.

For custom-number paints that hide iRacing-stamped number blocks:

```text
Documents\iRacing\paint\bmwm4gt3\car_num_<CustomerID>.tga
```

For a blank contingency/sponsor canvas, iRacing's current custom-paint article names:

```text
Documents\iRacing\paint\bmwm4gt3\car_decal_<CustomerID>.tga
```

The same article also describes a special 32-bit decal alpha texture for controlling template content from the `Turn Off Before Exporting TGA` section:

```text
Documents\iRacing\paint\bmwm4gt3\decal_<CustomerID>.tga
```

The alpha channel controls whether simulator/template decal content appears. White allows the item to show; black hides it, except for licensed/mandatory items that iRacing still requires.

For spec maps:

```text
Documents\iRacing\paint\bmwm4gt3\car_spec_<CustomerID>.tga
```

iRacing's documented PBR/spec-map channel model:

- Red: metallic
- Green: roughness
- Blue: clear coat
- Alpha: lighting-effect mask

The simulator can generate or update a `.mip` from a spec-map `.tga` when needed.

## Can We Edit This PSD?

Current state:

- We can inspect the PSD header, image resources, layers, visibility flags, group structure, and channel metadata with a small read-only parser.
- We can likely extract flattened or per-layer raster data with additional parsing work or a PSD-capable library.
- We should not treat direct PSD mutation as the first product path.
- I verified a metadata-only edit on the disposable local PSD by toggling the `wire` layer from hidden to visible. The edit changed the layer flags byte only; no pixel data or section lengths were rewritten. The `livery-poc/` folder is intentionally ignored and this PSD is not part of the git commit.

Direct PSD editing is possible in principle, but it is the wrong first target for TmrOverlay:

- PSD is a complex authoring format with groups, effects, blend modes, tagged blocks, ICC/XMP metadata, compatibility layers, thumbnails, and RLE/raw channel data.
- A partial PSD writer can easily corrupt a template even when the visible image appears simple.
- The local machine does not currently have ImageMagick, ExifTool, Pillow, or `psd-tools` installed.
- .NET libraries for PSD reading/writing exist, but write support and Photoshop compatibility would need a dedicated validation sweep.

### What We Can Safely Change In A Copy

The safest edits are small metadata changes that do not alter the length of any PSD section:

- Toggle layer visibility by flipping the hidden bit in the layer flags byte.
- Change layer opacity by editing the layer opacity byte.
- Change a group collapsed/open state by editing its section-divider type.
- Read layer names, bounds, blend modes, channel IDs, resource IDs, ICC/XMP metadata, and the compatibility image metadata.
- Potentially rename a layer only when the replacement fits the existing Pascal/Unicode name field shape. Arbitrary renames require rewriting length-bearing tagged blocks.

Concrete offsets found in this file:

| PSD layer | Meaning | Visible now | Flags offset | Opacity offset |
| --- | --- | ---: | ---: | ---: |
| `Paintable Area` layer | full-canvas paint layer | yes | `29204` | `29202` |
| `Car_decal` | decal layer | yes | `693316` | `693314` |
| `IMSA Car_decal` | alternate decal layer | no | `693682` | `693680` |
| `Paintable Area` group | main paint group | yes | `694060` | `694058` |
| `wire` | UV/wire guide | yes, after local test edit | `696602` | `696600` |
| `Turn Off Before Exporting TGA` group | export-disabled guide group | yes | `696968` | `696966` |
| `Base Paint` | spec-map base paint layer | yes | `698098` | `698096` |
| `metallic` | spec-map metallic layer | yes | `709858` | `709856` |
| `Custom Spec` group | spec authoring group | no | `716990` | `716988` |

This is enough for a future inspector or safe-copy utility to automate common template chores like "hide guide group before export" or "show wire overlay for alignment." It is not enough for a user-facing livery editor by itself.

### What Is Risky To Edit Directly

The hard part is pixel and structure mutation. This PSD has 57 layer records and 228 layer channel blocks. The parser found 172 RLE-compressed channel blocks and 56 raw channel blocks; the raw ones are mostly zero-size/group-like channels, while useful image layers are generally compressed.

Risky direct edits:

- Changing layer pixels requires decoding each color/transparency channel, recompositing the intended pixels, recompressing the channel data, updating channel byte counts, and updating the enclosing layer-info and layer/mask section lengths.
- Adding or removing a layer requires rewriting the whole layer record table, channel data region, section-divider markers, tagged blocks, and parent section lengths.
- Moving artwork between UV islands is not a simple image paste because the layer bounds, transparency channel, layer mask, blend mode, and any effects must still match Photoshop's PSD model.
- Editing text, vector shapes, smart objects, effects, or layer styles requires support for Photoshop tagged blocks beyond the basic raster layer records. This file already has effect/style tags such as `lfxs`, `lrFX`, `vstk`, and `shmd`.
- Updating the flattened compatibility image or thumbnails is separate from updating layers. A PSD can appear stale or confusing in external previewers if these are not kept in sync.

That means a robust product should not save user work by mutating this PSD in place. If we ever write PSDs, it should be a later import/export feature backed by a real PSD library and a Photoshop/iRacing validation matrix.

## Visualization Problem: UV Atlas Versus Human Car View

The main design pain is real: the PSD is a UV atlas, not a human-oriented drawing of a car. Hood, doors, roof, bumpers, mirrors, fenders, splitters, and small trim pieces are flattened into islands on a 2048 x 2048 texture. Those islands can be rotated, mirrored, split apart, or packed next to unrelated car surfaces. A stripe that feels like one continuous visual idea on the real car may cross several disconnected regions of the template.

A TmrOverlay livery editor should solve that mismatch directly instead of asking the user to mentally project the UV map onto the car.

Recommended editor model:

- Keep the PSD as a reference/import source.
- Build a per-car panel map that labels UV islands as semantic surfaces such as left door, right door, hood, roof, nose, rear bumper, wing, mirror, splitter, and small trim.
- Let the user work in human-facing panel views first: side, top, front, rear, and material/spec controls.
- Render the app-native project into the UV texture coordinates during export.
- Show both views at once: a semantic car-panel view for design intent and a UV atlas view for exact iRacing export placement.
- Treat seam-crossing artwork as a first-class problem. A door-to-fender stripe should be one editable design object that the renderer maps onto multiple UV islands.

The first useful version does not require direct PSD writing or a legal 3D car model. A strong 2D editor could still be much easier than Photoshop if it provides labeled panels, island highlighting, safe zones, wire/mask toggles, and a one-click TGA export.

The best later version is a 3D preview, but that depends on having a legitimate car mesh and matching UVs. Without that, the safer path is a labeled 2D panel preview plus the actual UV export map.

### Future Livery Editor Architecture

The editor should be built around an app-native project file rather than PSD mutation:

1. PSD/template inspector: read header, layers, groups, guides, masks, and useful metadata.
2. Car template registry: store the car folder name, texture size, panel labels, UV island polygons, guide-layer names, export-disabled layers, and spec-map conventions per car.
3. Livery project JSON: store colors, image decals, text, vector shapes, material controls, layer order, and which semantic panels each item targets.
4. Renderer: composite the project into a UV-space bitmap, using the per-car panel map to place artwork on the iRacing texture.
5. TGA exporter: write 24-bit RLE normal paint, 32-bit alpha decal/number variants, and optional spec maps.
6. Preview: start with UV atlas plus labeled panel views; add 3D only if a legitimate mesh/UV preview pipeline is available.
7. Optional PSD export: defer until the native editor and TGA export path are reliable.

Recommended product direction:

1. Treat PSD as an import/reference source, not as the app's saved authoring format.
2. Build a TmrOverlay-native livery project model: base color layers, decals/images/text, masks, UV guide visibility, number/sponsor safe zones, pattern references, and material channels.
3. Export iRacing-ready TGA files from that project model.
4. Optionally keep the original PSD next to the project for reference or round-tripping later.

This keeps the user-facing editor reliable without needing to preserve arbitrary Photoshop document internals.

## Future TmrOverlay Livery Editor Shape

A future livery editor should be an app/product surface, not a driving overlay tab. It should not be mixed into the normal race overlay list without a product pass.

Potential product shape:

- Entry point: a separate `Livery Studio` command from Settings or tray menu.
- Template import: choose a PSD or known iRacing template.
- Car identity: detect or ask for the iRacing car folder, e.g. `bmwm4gt3`.
- Customer/team ID: required before export naming.
- 2D editor: draw/edit decal, text, color, and pattern layers over the UV template.
- Preview: 2D flattened preview first; 3D preview later only if we have legal/technical access to the model/UV preview pipeline.
- Export:
  - `car_<CustomerID>.tga`
  - optional `car_num_<CustomerID>.tga`
  - optional `decal_<CustomerID>.tga`
  - optional `car_spec_<CustomerID>.tga`
- Safety: write to a staging folder first, then copy into `Documents\iRacing\paint\<car>`.
- Diagnostics: record export settings, file dimensions, TGA bit depth, RLE setting, target path, and source template identity.

Useful implementation slices:

1. Read-only PSD/template inspector in Core or tooling.
2. TGA writer and validator for 24-bit RLE and 32-bit alpha variants.
3. Simple project format with flat image/text layers.
4. Export to the correct iRacing paint folder.
5. Optional spec-map channel editor.
6. Optional import from common Photoshop templates into the app-native layer model.

Risks and open questions:

- We need the user's iRacing Customer ID or team paint target before filenames can be final.
- iRacing car folder names change over time; use the simulator-created paint folder when possible and keep a fallback lookup current.
- PSD template revisions may change for the BMW M4 GT3 / BMW M4 GT3 EVO naming and UV layout.
- Some stamped/licensed content cannot be removed even with decal alpha.
- Trading Paints distribution rules and iRacing paint policy need a separate product/legal review before any sharing/upload feature.

## Sources

- iRacing custom paint instructions: https://support.iracing.com/support/solutions/articles/31000133480-how-do-i-custom-paint-my-iracing-cars-
- iRacing active car filepath table: https://support.iracing.com/support/solutions/articles/31000172625-filepath-for-active-iracing-cars
- iRacing spec-map / paint texture notes: https://www.iracing.com/custom-paint-textures/
- iRacing paint policy: https://www.iracing.com/paint-policy/
