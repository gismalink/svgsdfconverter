# SVG SDF Converter

Unity package for converting SVG artwork into SDF textures and rendering it with the included shader.

## Installation

In Unity Package Manager, select **Add package from git URL** and enter:

```text
https://github.com/gismalink/svgsdfconverter.git#v1.0.5
```

For local development, select **Add package from disk** and choose this package's `package.json`.

## Usage

1. Open **Tools > SVG SDF Generator**.
2. Add or drag SVG assets from the project.
3. Set raster resolution, padding, and maximum distance.
4. Generate the textures. Each `_SDF.png` is saved beside its source SVG.
5. Create a material with **SVG SDF/SDF** and assign the generated texture.

Keep source SVGs under the project's Assets folder so output files are writable.

## Package layout

- `Editor/`: converter, window, and Editor-only assembly definition.
- `Runtime/Shaders/SDF.shader`: shader available in Player builds.
- `Documentation~/Usage.md`: texture encoding and import settings.

Existing script and shader GUIDs are preserved when migrating from ECar.

## Dissolve visibility

`_DisolveAlpha` works with Poster AnimatedDisolveVisibility through MaterialPropertyBlock: 1 is fully visible, 0 is hidden. Screen-space deterministic Simple Noise matches Lit_Env_Midpoly at the default noise scale of 500. Use Dissolve Noise Scale to match other poster materials.
