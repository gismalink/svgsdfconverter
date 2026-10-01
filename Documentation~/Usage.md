# Texture encoding

The signed distance is stored in the red channel: values below 0.5 are outside, 0.5 is the contour, and values above 0.5 are inside.

The generator configures linear sampling (sRGB disabled), no lossy compression, bilinear filtering, Clamp wrapping, and no mipmaps. The matching shader defaults to a threshold of 0.5; color, softness, and outline are material settings.

The converter runs only in the Unity Editor. SVG geometry is rasterized by the converter; SVG features are limited to those implemented in SDFGenerator and should be checked in the generated output.

## Suggested Max Distance

The UI suggests `max(2, resolution / 64)` pixels: 128 → 2, 256 → 4, 512 → 8, 1024 → 16, 2048 → 32, 4096 → 64. This is a package-specific starting heuristic preserving the previous 32px default at 2048, not a universal SDF standard or a measured optimum for a particular SVG. Click Apply to use it; manual settings are preserved otherwise.

Smaller ranges provide finer distance precision for thin lines; larger ranges support wider outlines and effects. Padding at least as large as Max Distance preserves the full outer distance range near texture borders.
