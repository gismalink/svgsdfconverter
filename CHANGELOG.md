# Changelog

## 1.0.5

- Renamed noise helpers and visibility header to generic Dissolve names.
- Preserved material properties and visibility behavior for compatibility.

## 1.0.4

- Added Poster-compatible _DisolveAlpha visibility using deterministic screen-space Simple Noise.
- Default noise scale matches Lit_Env_Midpoly (500); visibility defaults to 1.

## 1.0.3

- Show a resolution-dependent Max Distance suggestion with an Apply button.
- Explain range tradeoffs and padding requirements.

## 1.0.2

- Added 128 and 256 raster resolution options.

## 1.0.1

- Default shape threshold is 0.5 and softness is exactly 0.
- Allow zero softness and render it as a hard edge without smoothstep on equal bounds.

## 1.0.0

- Extracted SVG converter and matching SDF shader from ECar.
- Isolated converter scripts in an Editor-only assembly.
- Preserved existing Unity asset GUIDs.
