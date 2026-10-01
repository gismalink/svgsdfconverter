using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class SDFGenerator
{
    // ============================================================
    // PUBLIC API
    // ============================================================

    public static Texture2D Generate(
        Texture2D source,
        float maxDistance = 32f,
        float alphaThreshold = 0.5f)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        Color32[] pixels = source.GetPixels32();

        int w = source.width;
        int h = source.height;

        bool[,] inside = new bool[w, h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                inside[x, y] =
                    pixels[y * w + x].a / 255f >= alphaThreshold;
            }
        }

        return BuildSDF(
            inside,
            maxDistance);
    }

    public static Texture2D GenerateFromSVG(
        string svgPath,
        int rasterResolution = 2048,
        float maxDistance = 32f,
        int paddingPixels = 32)
    {
        //codex-agent
        rasterResolution =
            Mathf.Clamp(rasterResolution, 1, 4096);

        maxDistance =
            Mathf.Max(0.001f, maxDistance);

        paddingPixels =
            Mathf.Clamp(
                paddingPixels,
                0,
                rasterResolution / 4);

        if (!File.Exists(svgPath))
            throw new FileNotFoundException(
                "SVG not found",
                svgPath);

        XDocument doc = XDocument.Load(svgPath);

        XElement root = doc.Root;

        if (root == null)
            throw new Exception("Invalid SVG.");

        float width =
            ReadSize(
                root,
                "width",
                100);

        float height =
            ReadSize(
                root,
                "height",
                100);

        string viewBox =
            Attr(root, "viewBox");

        if (!string.IsNullOrEmpty(viewBox))
        {
            string[] v =
                Regex.Split(
                    viewBox.Trim(),
                    @"[\s,]+");

            if (v.Length >= 4)
            {
                width = F(v[2]);
                height = F(v[3]);
            }
        }

        width = Mathf.Max(0.001f, width);
        height = Mathf.Max(0.001f, height);

        float scale =
            (rasterResolution -
             paddingPixels * 2) /
            Mathf.Max(width, height);

        // Keep the high-resolution working buffer bounded. A 4096 output
        // texture with 2x supersampling would otherwise require an 8192px
        // distance transform and several hundred megabytes of temporary RAM.
        int supersample =
            rasterResolution <= 512
                ? 4
                : rasterResolution <= 2048
                    ? 2
                    : 1;

        int finalW =
            Mathf.Max(
                1,
                Mathf.RoundToInt(width * scale) +
                paddingPixels * 2);

        int finalH =
            Mathf.Max(
                1,
                Mathf.RoundToInt(height * scale) +
                paddingPixels * 2);

        int rw = finalW * supersample;
        int rh = finalH * supersample;

        float paddingWorld =
            paddingPixels / scale;

        Raster raster =
            new Raster(
                rw,
                rh,
                -paddingWorld,
                -paddingWorld,
                width + paddingWorld * 2f,
                height + paddingWorld * 2f);

        DrawElement(
            root,
            Matrix3x3.identity,
            raster,
            null);

        bool[,] inside =
            new bool[
                raster.Width,
                raster.Height];

        for (int y = 0;
             y < raster.Height;
             y++)
        {
            for (int x = 0;
                 x < raster.Width;
                 x++)
            {
                inside[x, y] =
                    raster.IsInside(
                        x,
                        y,
                        0.5f);
            }
        }

        Texture2D highRes =
            BuildSDF(
                inside,
                maxDistance * supersample);

        Texture2D result =
            DownsampleSDF(
                highRes,
                finalW,
                finalH);

        UnityEngine.Object.DestroyImmediate(
            highRes);

        result.name = "SDF";

        return result;
    }

#if UNITY_EDITOR

    public static void SavePNG(
        Texture2D texture,
        string path)
    {
        if (texture == null)
            throw new ArgumentNullException(
                nameof(texture));

        string dir =
            Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(
            path,
            texture.EncodeToPNG());

        //codex-agent
        AssetDatabase.ImportAsset(
            path,
            ImportAssetOptions.ForceUpdate);
    }

    //codex-agent
    public static void ConfigureImporter(
        string path)
    {
        TextureImporter importer =
            AssetImporter.GetAtPath(path)
            as TextureImporter;

        if (importer == null)
            return;

        importer.textureType =
            TextureImporterType.Default;

        importer.alphaIsTransparency =
            false;

        importer.mipmapEnabled =
            false;

        importer.npotScale =
            TextureImporterNPOTScale.None;

        importer.filterMode =
            FilterMode.Bilinear;

        importer.wrapMode =
            TextureWrapMode.Clamp;

        importer.sRGBTexture =
            false;

        // SDF stores the signed distance in the colour value itself. Any
        // lossy texture compression shifts that value around the 0.5 edge
        // threshold and creates visibly stepped contours.
        importer.textureCompression =
            TextureImporterCompression.Uncompressed;

        importer.SaveAndReimport();
    }

#endif

    // ============================================================
    // SDF
    // ============================================================

    private static Texture2D BuildSDF(
        bool[,] inside,
        float maxDistance)
    {
        int w =
            inside.GetLength(0);

        int h =
            inside.GetLength(1);

        byte[] data =
            new byte[w * h];

        float distanceScale =
            Mathf.Max(
                1f,
                maxDistance);

        //codex-agent
        WriteDistanceValues(
            data,
            inside,
            DistanceTransform(inside, false),
            false,
            distanceScale);

        WriteDistanceValues(
            data,
            inside,
            DistanceTransform(inside, true),
            true,
            distanceScale);

        Texture2D tex =
            new Texture2D(
                w,
                h,
                TextureFormat.R8,
                false,
                true);

        tex.name = "SDF";

        tex.SetPixelData(
            data,
            0);

        tex.Apply(
            false,
            false);

        return tex;
    }

    //codex-agent
    private static void WriteDistanceValues(
        byte[] destination,
        bool[,] inside,
        float[,] distances,
        bool writeInside,
        float distanceScale)
    {
        int width = inside.GetLength(0);
        int height = inside.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (inside[x, y] != writeInside)
                    continue;

                float normalizedDistance = Mathf.Clamp01(
                    distances[x, y] / distanceScale);

                float value = writeInside
                    ? 0.5f + normalizedDistance * 0.5f
                    : 0.5f - normalizedDistance * 0.5f;

                destination[y * width + x] =
                    (byte)Mathf.RoundToInt(value * 255f);
            }
        }
    }

    private static float[,] DistanceTransform(
        bool[,] mask,
        bool target)
    {
        int width =
            mask.GetLength(0);

        int height =
            mask.GetLength(1);

        float[,] temp =
            new float[
                width,
                height];

        float[,] result =
            new float[
                width,
                height];

        int max =
            Mathf.Max(
                width,
                height);

        float[] f =
            new float[max];

        float[] d =
            new float[max];

        int[] v =
            new int[max];

        float[] z =
            new float[max + 1];

        for (int y = 0;
             y < height;
             y++)
        {
            for (int x = 0;
                 x < width;
                 x++)
            {
                f[x] =
                    mask[x, y] == target
                        ? float.PositiveInfinity
                        : 0f;
            }

            Distance1D(
                f,
                width,
                d,
                v,
                z);

            for (int x = 0;
                 x < width;
                 x++)
            {
                temp[x, y] =
                    d[x];
            }
        }

        for (int x = 0;
             x < width;
             x++)
        {
            for (int y = 0;
                 y < height;
                 y++)
            {
                f[y] =
                    temp[x, y];
            }

            Distance1D(
                f,
                height,
                d,
                v,
                z);

            for (int y = 0;
                 y < height;
                 y++)
            {
                float value =
                    d[y];

                if (float.IsInfinity(value))
                {
                    result[x, y] =
                        max;
                }
                else
                {
                    result[x, y] =
                        Mathf.Sqrt(
                            Mathf.Max(
                                0f,
                                value));
                }
            }
        }

        return result;
    }

    private static void Distance1D(
        float[] f,
        int n,
        float[] d,
        int[] v,
        float[] z)
    {
        int first =
            -1;

        for (int i = 0;
             i < n;
             i++)
        {
            if (!float.IsInfinity(f[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            for (int i = 0;
                 i < n;
                 i++)
            {
                d[i] =
                    float.PositiveInfinity;
            }

            return;
        }

        int k = 0;

        v[0] = first;

        z[0] =
            float.NegativeInfinity;

        z[1] =
            float.PositiveInfinity;

        for (int q = first + 1;
             q < n;
             q++)
        {
            float s;

            while (true)
            {
                int vk =
                    v[k];

                float denominator =
                    2f *
                    (q - vk);

                if (denominator == 0f)
                {
                    s =
                        float.PositiveInfinity;
                }
                else
                {
                    s =
                        ((f[q] + q * q) -
                         (f[vk] + vk * vk)) /
                        denominator;
                }

                if (s > z[k])
                    break;

                if (k == 0)
                    break;

                k--;
            }

            if (s <= z[k])
                s =
                    float.NegativeInfinity;

            k++;

            v[k] = q;

            z[k] = s;

            z[k + 1] =
                float.PositiveInfinity;
        }

        k = 0;

        for (int q = 0;
             q < n;
             q++)
        {
            while (
                z[k + 1] < q)
            {
                k++;
            }

            float dx =
                q - v[k];

            d[q] =
                dx * dx +
                f[v[k]];
        }
    }

    private static Texture2D DownsampleSDF(
        Texture2D source,
        int width,
        int height)
    {
        Texture2D result =
            new Texture2D(
                width,
                height,
                TextureFormat.R8,
                false,
                true);

        Color32[] src =
            source.GetPixels32();

        byte[] output =
            new byte[
                width * height];

        int sw =
            source.width;

        int sh =
            source.height;

        for (int y = 0;
             y < height;
             y++)
        {
            float fy =
                (y + 0.5f) /
                height *
                sh -
                0.5f;

            int y0 =
                Mathf.Clamp(
                    Mathf.FloorToInt(fy),
                    0,
                    sh - 1);

            int y1 =
                Mathf.Clamp(
                    y0 + 1,
                    0,
                    sh - 1);

            float ty =
                Mathf.Clamp01(
                    fy - y0);

            for (int x = 0;
                 x < width;
                 x++)
            {
                float fx =
                    (x + 0.5f) /
                    width *
                    sw -
                0.5f;

                int x0 =
                    Mathf.Clamp(
                        Mathf.FloorToInt(fx),
                        0,
                        sw - 1);

                int x1 =
                    Mathf.Clamp(
                        x0 + 1,
                        0,
                        sw - 1);

                float tx =
                    Mathf.Clamp01(
                        fx - x0);

                float a =
                    src[
                        y0 * sw + x0].r;

                float b =
                    src[
                        y0 * sw + x1].r;

                float c =
                    src[
                        y1 * sw + x0].r;

                float d =
                    src[
                        y1 * sw + x1].r;

                float value =
                    Mathf.Lerp(
                        Mathf.Lerp(
                            a,
                            b,
                            tx),
                        Mathf.Lerp(
                            c,
                            d,
                            tx),
                        ty);

                output[
                    y * width + x] =
                    (byte)Mathf.RoundToInt(
                        value);
            }
        }

        result.SetPixelData(
            output,
            0);

        result.Apply(
            false,
            false);

        return result;
    }

    // ============================================================
    // SVG DRAWING
    // ============================================================

    private static void DrawElement(
        XElement e,
        Matrix3x3 parent,
        Raster raster,
        Style inherited)
    {
        string name =
            e.Name.LocalName;

        Style style =
            Style.Read(
                e,
                inherited);

        Matrix3x3 transform =
            parent *
            ParseTransform(
                Attr(
                    e,
                    "transform"));

        if (name == "path")
        {
            string d =
                Attr(
                    e,
                    "d");

            if (!string.IsNullOrEmpty(d))
            {
                List<List<Vector2>> paths =
                    ParsePath(d);

                for (int i = 0;
                     i < paths.Count;
                     i++)
                {
                    Transform(
                        paths[i],
                        transform);
                }

                raster.FillCompound(
                    paths,
                    style);

                if (style.strokeEnabled &&
                    style.strokeWidth > 0f)
                {
                    for (int i = 0;
                         i < paths.Count;
                         i++)
                    {
                        raster.Stroke(
                            paths[i],
                            style);
                    }
                }
            }
        }

        else if (name == "rect")
        {
            float x =
                Num(e, "x");

            float y =
                Num(e, "y");

            float w =
                Num(e, "width");

            float h =
                Num(e, "height");

            if (w <= 0f ||
                h <= 0f)
            {
                goto DrawChildren;
            }

            float rx =
                Num(e, "rx");

            float ry =
                Num(e, "ry");

            if (rx > 0f &&
                ry <= 0f)
            {
                ry = rx;
            }

            if (ry > 0f &&
                rx <= 0f)
            {
                rx = ry;
            }

            rx =
                Mathf.Clamp(
                    Mathf.Abs(rx),
                    0f,
                    w * 0.5f);

            ry =
                Mathf.Clamp(
                    Mathf.Abs(ry),
                    0f,
                    h * 0.5f);

            List<Vector2> p =
                RoundedRect(
                    x,
                    y,
                    w,
                    h,
                    rx,
                    ry,
                    ArcSegments(
                        Mathf.Max(rx, ry),
                        raster.GetPixelScale()));

            Transform(
                p,
                transform);

            raster.FillCompound(
                new List<List<Vector2>>
                {
                    p
                },
                style);

            if (style.strokeEnabled &&
                style.strokeWidth > 0f)
            {
                raster.Stroke(
                    p,
                    style);
            }
        }

        else if (name == "circle")
        {
            float cx =
                Num(e, "cx");

            float cy =
                Num(e, "cy");

            float r =
                Num(e, "r");

            if (r > 0f)
            {
                List<Vector2> p =
                    Ellipse(
                        cx,
                        cy,
                        r,
                        r,
                        ArcSegments(
                            r,
                            raster.GetPixelScale()));

                Transform(
                    p,
                    transform);

                raster.FillCompound(
                    new List<List<Vector2>>
                    {
                        p
                    },
                    style);

                if (style.strokeEnabled)
                {
                    raster.Stroke(
                        p,
                        style);
                }
            }
        }

        else if (name == "ellipse")
        {
            float cx =
                Num(e, "cx");

            float cy =
                Num(e, "cy");

            float rx =
                Num(e, "rx");

            float ry =
                Num(e, "ry");

            if (rx > 0f &&
                ry > 0f)
            {
                List<Vector2> p =
                    Ellipse(
                        cx,
                        cy,
                        rx,
                        ry,
                        ArcSegments(
                            Mathf.Max(rx, ry),
                            raster.GetPixelScale()));

                Transform(
                    p,
                    transform);

                raster.FillCompound(
                    new List<List<Vector2>>
                    {
                        p
                    },
                    style);

                if (style.strokeEnabled)
                {
                    raster.Stroke(
                        p,
                        style);
                }
            }
        }

        else if (name == "line")
        {
            float x1 =
                Num(e, "x1");

            float y1 =
                Num(e, "y1");

            float x2 =
                Num(e, "x2");

            float y2 =
                Num(e, "y2");

            if (style.strokeEnabled &&
                style.strokeWidth > 0f)
            {
                List<Vector2> p =
                    new List<Vector2>
                    {
                        transform.MultiplyPoint(
                            new Vector2(
                                x1,
                                y1)),

                        transform.MultiplyPoint(
                            new Vector2(
                                x2,
                                y2))
                    };

                raster.Stroke(
                    p,
                    style);
            }
        }

        else if (
            name == "polygon" ||
            name == "polyline")
        {
            List<Vector2> p =
                ParsePoints(
                    Attr(
                        e,
                        "points"));

            Transform(
                p,
                transform);

            if (name == "polygon" &&
                p.Count >= 3)
            {
                if (!Approximately(
                        p[0],
                        p[p.Count - 1]))
                {
                    p.Add(p[0]);
                }

                raster.FillCompound(
                    new List<List<Vector2>>
                    {
                        p
                    },
                    style);
            }

            if (style.strokeEnabled &&
                p.Count >= 2)
            {
                raster.Stroke(
                    p,
                    style);
            }
        }

#if UNITY_EDITOR

        else if (name == "text")
        {
            DrawText(
                e,
                transform,
                raster,
                style);
        }

#endif

DrawChildren:

        foreach (XElement child
                 in e.Elements())
        {
            DrawElement(
                child,
                transform,
                raster,
                style);
        }
    }

#if UNITY_EDITOR

    private static void DrawText(
        XElement e,
        Matrix3x3 transform,
        Raster raster,
        Style style)
    {
        if (!style.fillEnabled)
            return;

        string text =
            e.Value;

        if (string.IsNullOrEmpty(text))
            return;

        text =
            Regex.Replace(
                text,
                @"\s+",
                " ");

        string family =
            Attr(
                e,
                "font-family");

        if (string.IsNullOrEmpty(family))
            family = "Arial";

        int fontSize =
            Mathf.RoundToInt(
                Num(
                    e,
                    "font-size"));

        if (fontSize <= 1)
            fontSize = 16;

        Font font = null;

        try
        {
            font =
                Font.CreateDynamicFontFromOSFont(
                    family,
                    fontSize);
        }
        catch
        {
            try
            {
                font =
                    Font.CreateDynamicFontFromOSFont(
                        "Arial",
                        fontSize);
            }
            catch
            {
                return;
            }
        }

        if (font == null)
            return;

        try
        {
            font.RequestCharactersInTexture(
                text,
                fontSize,
                FontStyle.Normal);

            float x =
                Num(
                    e,
                    "x");

            float y =
                Num(
                    e,
                    "y");

            Texture2D atlas =
                font.material != null
                    ? font.material.mainTexture
                        as Texture2D
                    : null;

            if (atlas == null)
                return;

            Color[] atlasPixels =
                atlas.GetPixels();

            int atlasW =
                atlas.width;

            int atlasH =
                atlas.height;

            foreach (char ch in text)
            {
                CharacterInfo info;

                if (!font.GetCharacterInfo(
                        ch,
                        out info,
                        fontSize,
                        FontStyle.Normal))
                {
                    continue;
                }

                int glyphWidth =
                    Mathf.Abs(
                        info.glyphWidth);

                int glyphHeight =
                    Mathf.Abs(
                        info.glyphHeight);

                if (glyphWidth <= 0 ||
                    glyphHeight <= 0)
                {
                    x += info.advance;
                    continue;
                }

                Rect uv =
                    info.uv;

                int sx =
                    Mathf.Clamp(
                        Mathf.RoundToInt(
                            uv.x *
                            atlasW),
                        0,
                        atlasW - 1);

                int sy =
                    Mathf.Clamp(
                        Mathf.RoundToInt(
                            uv.y *
                            atlasH),
                        0,
                        atlasH - 1);

                int sw =
                    Mathf.Max(
                        1,
                        Mathf.RoundToInt(
                            Mathf.Abs(
                                uv.width) *
                            atlasW));

                int sh =
                    Mathf.Max(
                        1,
                        Mathf.RoundToInt(
                            Mathf.Abs(
                                uv.height) *
                            atlasH));

                for (int py = 0;
                     py < glyphHeight;
                     py++)
                {
                    for (int px = 0;
                         px < glyphWidth;
                         px++)
                    {
                        float u =
                            (px + 0.5f) /
                            glyphWidth;

                        float v =
                            (py + 0.5f) /
                            glyphHeight;

                        int ax =
                            Mathf.Clamp(
                                sx +
                                Mathf.RoundToInt(
                                    u *
                                    sw),
                                0,
                                atlasW - 1);

                        int ay =
                            Mathf.Clamp(
                                sy +
                                Mathf.RoundToInt(
                                    v *
                                    sh),
                                0,
                                atlasH - 1);

                        Color c =
                            atlasPixels[
                                ay *
                                atlasW +
                                ax];

                        if (c.a <= 0.001f)
                            continue;

                        Vector2 local =
                            new Vector2(
                                x +
                                info.minX +
                                px,

                                y +
                                info.minY +
                                py);

                        Vector2 world =
                            transform.MultiplyPoint(
                                local);

                        raster.SetAlphaPixel(
                            world,
                            style.fillColor,
                            c.a *
                            style.opacity *
                            style.fillOpacity);
                    }
                }

                x += info.advance;
            }
        }
        catch
        {
            /*
             * Some Unity versions don't expose the
             * dynamic font atlas as readable texture.
             */
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                font);
        }
    }

#endif

    // ============================================================
    // PATH PARSER
    // ============================================================

    private static List<List<Vector2>> ParsePath(
        string d)
    {
        List<string> tokens =
            new List<string>();

        foreach (Match m in Regex.Matches(
            d,
            @"[a-zA-Z]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?"))
        {
            tokens.Add(
                m.Value);
        }

        List<List<Vector2>> result =
            new List<List<Vector2>>();

        List<Vector2> path = null;

        Vector2 current =
            Vector2.zero;

        Vector2 start =
            Vector2.zero;

        Vector2 lastControl =
            Vector2.zero;

        char cmd = ' ';

        char previousCommand = ' ';

        int iToken = 0;

        while (iToken < tokens.Count)
        {
            if (IsCommand(
                    tokens[iToken]))
            {
                cmd =
                    tokens[iToken++][0];
            }

            if (cmd == ' ')
                break;

            bool relative =
                char.IsLower(cmd);

            char c =
                char.ToUpper(cmd);

            int count =
                ParamCount(c);

            if (count == 0)
            {
                if (c == 'Z')
                {
                    if (path != null &&
                        path.Count > 1)
                    {
                        if (!Approximately(
                                path[path.Count - 1],
                                start))
                        {
                            path.Add(start);
                        }

                        current =
                            start;
                    }

                    lastControl =
                        current;

                    previousCommand =
                        'Z';

                    cmd = ' ';

                    continue;
                }

                break;
            }

            if (iToken + count >
                tokens.Count)
            {
                break;
            }

            float[] p =
                new float[count];

            bool valid = true;

            for (int k = 0;
                 k < count;
                 k++)
            {
                if (iToken >= tokens.Count ||
                    IsCommand(
                        tokens[iToken]))
                {
                    valid = false;
                    break;
                }

                p[k] =
                    F(
                        tokens[
                            iToken++]);
            }

            if (!valid)
                break;

            switch (c)
            {
                case 'M':
                {
                    Vector2 v =
                        new Vector2(
                            p[0],
                            p[1]);

                    if (relative)
                        v += current;

                    path =
                        new List<Vector2>();

                    result.Add(path);

                    path.Add(v);

                    current = v;
                    start = v;
                    lastControl = v;

                    cmd =
                        relative
                            ? 'l'
                            : 'L';

                    previousCommand =
                        'M';

                    break;
                }

                case 'L':
                {
                    Vector2 v =
                        new Vector2(
                            p[0],
                            p[1]);

                    if (relative)
                        v += current;

                    EnsurePath(
                        ref path,
                        result,
                        current);

                    path.Add(v);

                    current = v;
                    lastControl = v;

                    previousCommand =
                        'L';

                    break;
                }

                case 'H':
                {
                    float xx =
                        relative
                            ? current.x + p[0]
                            : p[0];

                    Vector2 v =
                        new Vector2(
                            xx,
                            current.y);

                    EnsurePath(
                        ref path,
                        result,
                        current);

                    path.Add(v);

                    current = v;
                    lastControl = v;

                    previousCommand =
                        'H';

                    break;
                }

                case 'V':
                {
                    float yy =
                        relative
                            ? current.y + p[0]
                            : p[0];

                    Vector2 v =
                        new Vector2(
                            current.x,
                            yy);

                    EnsurePath(
                        ref path,
                        result,
                        current);

                    path.Add(v);

                    current = v;
                    lastControl = v;

                    previousCommand =
                        'V';

                    break;
                }

                case 'C':
                {
                    Vector2 c1 =
                        new Vector2(
                            p[0],
                            p[1]);

                    Vector2 c2 =
                        new Vector2(
                            p[2],
                            p[3]);

                    Vector2 v =
                        new Vector2(
                            p[4],
                            p[5]);

                    if (relative)
                    {
                        c1 += current;
                        c2 += current;
                        v += current;
                    }

                    EnsurePath(
                        ref path,
                        result,
                        current);

                    AddCubic(
                        path,
                        current,
                        c1,
                        c2,
                        v);

                    current = v;
                    lastControl = c2;

                    previousCommand =
                        'C';

                    break;
                }

                case 'S':
                {
                    Vector2 c1;

                    if (previousCommand == 'C' ||
                        previousCommand == 'S')
                    {
                        c1 =
                            current * 2f -
                            lastControl;
                    }
                    else
                    {
                        c1 =
                            current;
                    }

                    Vector2 c2 =
                        new Vector2(
                            p[0],
                            p[1]);

                    Vector2 v =
                        new Vector2(
                            p[2],
                            p[3]);

                    if (relative)
                    {
                        c2 += current;
                        v += current;
                    }

                    EnsurePath(
                        ref path,
                        result,
                        current);

                    AddCubic(
                        path,
                        current,
                        c1,
                        c2,
                        v);

                    current = v;
                    lastControl = c2;

                    previousCommand =
                        'S';

                    break;
                }

                case 'Q':
                {
                    Vector2 control =
                        new Vector2(
                            p[0],
                            p[1]);

                    Vector2 v =
                        new Vector2(
                            p[2],
                            p[3]);

                    if (relative)
                    {
                        control += current;
                        v += current;
                    }

                    EnsurePath(
                        ref path,
                        result,
                        current);

                    AddQuadratic(
                        path,
                        current,
                        control,
                        v);

                    current = v;
                    lastControl = control;

                    previousCommand =
                        'Q';

                    break;
                }

                case 'T':
                {
                    Vector2 control;

                    if (previousCommand == 'Q' ||
                        previousCommand == 'T')
                    {
                        control =
                            current * 2f -
                            lastControl;
                    }
                    else
                    {
                        control =
                            current;
                    }

                    Vector2 v =
                        new Vector2(
                            p[0],
                            p[1]);

                    if (relative)
                        v += current;

                    EnsurePath(
                        ref path,
                        result,
                        current);

                    AddQuadratic(
                        path,
                        current,
                        control,
                        v);

                    current = v;
                    lastControl = control;

                    previousCommand =
                        'T';

                    break;
                }

                case 'A':
                {
                    Vector2 v =
                        new Vector2(
                            p[5],
                            p[6]);

                    if (relative)
                        v += current;

                    EnsurePath(
                        ref path,
                        result,
                        current);

                    AddArc(
                        path,
                        current,
                        p[0],
                        p[1],
                        p[2],
                        p[3] != 0,
                        p[4] != 0,
                        v);

                    current = v;
                    lastControl = v;

                    previousCommand =
                        'A';

                    break;
                }
            }
        }

        return result;
    }

    private static void AddCubic(
        List<Vector2> path,
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        Vector2 p3)
    {
        float length =
            Vector2.Distance(p0, p1) +
            Vector2.Distance(p1, p2) +
            Vector2.Distance(p2, p3);

        int steps =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    length * 2f),
                12,
                512);

        for (int i = 1;
             i <= steps;
             i++)
        {
            float t =
                i / (float)steps;

            float u =
                1f - t;

            path.Add(
                u * u * u * p0 +
                3f * u * u * t * p1 +
                3f * u * t * t * p2 +
                t * t * t * p3);
        }
    }

    private static void AddQuadratic(
        List<Vector2> path,
        Vector2 p0,
        Vector2 p1,
        Vector2 p2)
    {
        float length =
            Vector2.Distance(p0, p1) +
            Vector2.Distance(p1, p2);

        int steps =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    length * 2f),
                12,
                384);

        for (int i = 1;
             i <= steps;
             i++)
        {
            float t =
                i / (float)steps;

            float u =
                1f - t;

            path.Add(
                u * u * p0 +
                2f * u * t * p1 +
                t * t * p2);
        }
    }

    private static void AddArc(
        List<Vector2> path,
        Vector2 p0,
        float rx,
        float ry,
        float rotation,
        bool largeArc,
        bool sweep,
        Vector2 p1)
    {
        if (rx <= 0f ||
            ry <= 0f)
        {
            path.Add(p1);
            return;
        }

        rx =
            Mathf.Abs(rx);

        ry =
            Mathf.Abs(ry);

        float phi =
            rotation *
            Mathf.Deg2Rad;

        float cosPhi =
            Mathf.Cos(phi);

        float sinPhi =
            Mathf.Sin(phi);

        Vector2 d =
            (p0 - p1) *
            0.5f;

        float x =
            cosPhi * d.x +
            sinPhi * d.y;

        float y =
            -sinPhi * d.x +
            cosPhi * d.y;

        float scale =
            x * x /
            (rx * rx) +
            y * y /
            (ry * ry);

        if (scale > 1f)
        {
            float s =
                Mathf.Sqrt(scale);

            rx *= s;
            ry *= s;
        }

        float rx2 =
            rx * rx;

        float ry2 =
            ry * ry;

        float num =
            rx2 * ry2 -
            rx2 * y * y -
            ry2 * x * x;

        float den =
            rx2 * y * y +
            ry2 * x * x;

        float factor =
            den < 0.000001f
                ? 0f
                : Mathf.Sqrt(
                    Mathf.Max(
                        0f,
                        num / den));

        if (largeArc == sweep)
            factor = -factor;

        float cxp =
            factor *
            rx *
            y /
            ry;

        float cyp =
            -factor *
            ry *
            x /
            rx;

        float cx =
            cosPhi * cxp -
            sinPhi * cyp +
            (p0.x + p1.x) *
            0.5f;

        float cy =
            sinPhi * cxp +
            cosPhi * cyp +
            (p0.y + p1.y) *
            0.5f;

        Vector2 u =
            new Vector2(
                (x - cxp) / rx,
                (y - cyp) / ry);

        Vector2 v =
            new Vector2(
                (-x - cxp) / rx,
                (-y - cyp) / ry);

        float start =
            Mathf.Atan2(
                u.y,
                u.x);

        float delta =
            Mathf.Atan2(
                u.x * v.y -
                u.y * v.x,
                Vector2.Dot(
                    u,
                    v));

        if (!sweep &&
            delta > 0f)
        {
            delta -=
                Mathf.PI * 2f;
        }

        if (sweep &&
            delta < 0f)
        {
            delta +=
                Mathf.PI * 2f;
        }

        int steps =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    Mathf.Abs(delta) *
                    64f),
                12,
                512);

        for (int i = 1;
             i <= steps;
             i++)
        {
            float a =
                start +
                delta *
                i /
                steps;

            float ca =
                Mathf.Cos(a);

            float sa =
                Mathf.Sin(a);

            path.Add(
                new Vector2(
                    cx +
                    rx * ca * cosPhi -
                    ry * sa * sinPhi,

                    cy +
                    rx * ca * sinPhi +
                    ry * sa * cosPhi));
        }
    }

    // ============================================================
    // RASTER
    // ============================================================

    private sealed class Raster
    {
        public int Width;
        public int Height;

        private float worldWidth;
        private float worldHeight;
        private float worldMinX;
        private float worldMinY;

        private byte[] alpha;

        public Raster(
            int width,
            int height,
            float worldMinX,
            float worldMinY,
            float worldWidth,
            float worldHeight)
        {
            Width = width;
            Height = height;

            this.worldWidth =
                worldWidth;

            this.worldHeight =
                worldHeight;

            this.worldMinX =
                worldMinX;

            this.worldMinY =
                worldMinY;

            alpha =
                new byte[
                    width *
                    height];
        }

        public bool IsInside(
            int x,
            int y,
            float threshold)
        {
            return alpha[
                y * Width + x] >=
                threshold * 255f;
        }

        public void FillCompound(
            List<List<Vector2>> worldPaths,
            Style style)
        {
            if (!style.fillEnabled ||
                worldPaths == null)
            {
                return;
            }

            Color color =
                style.fillColor;

            color.a *=
                style.opacity *
                style.fillOpacity;

            if (color.a <= 0.001f)
                return;

            List<List<Vector2>> paths =
                new List<List<Vector2>>();

            int minY =
                Height;

            int maxY =
                -1;

            foreach (List<Vector2> world
                     in worldPaths)
            {
                if (world == null ||
                    world.Count < 3)
                {
                    continue;
                }

                List<Vector2> p =
                    new List<Vector2>(
                        world.Count);

                foreach (Vector2 v
                         in world)
                {
                    Vector2 q =
                        WorldToPixel(v);

                    p.Add(q);

                    minY =
                        Mathf.Min(
                            minY,
                            Mathf.FloorToInt(
                                q.y));

                    maxY =
                        Mathf.Max(
                            maxY,
                            Mathf.CeilToInt(
                                q.y));
                }

                paths.Add(p);
            }

            if (paths.Count == 0)
                return;

            minY =
                Mathf.Clamp(
                    minY,
                    0,
                    Height - 1);

            maxY =
                Mathf.Clamp(
                    maxY,
                    0,
                    Height - 1);

            //codex-agent
            List<ScanIntersection> intersections =
                new List<ScanIntersection>();

            for (int y = minY;
                 y <= maxY;
                 y++)
            {
                float scanY =
                    y + 0.5f;

                intersections.Clear();

                foreach (List<Vector2> path
                         in paths)
                {
                    if (path.Count < 3)
                        continue;

                    for (int i = 0;
                         i < path.Count;
                         i++)
                    {
                        Vector2 a =
                            path[i];

                        Vector2 b =
                            path[
                                (i + 1) %
                                path.Count];

                        if (Mathf.Abs(
                                a.y - b.y)
                            < 0.000001f)
                        {
                            continue;
                        }

                        bool cross =
                            (a.y <= scanY &&
                             b.y > scanY) ||
                            (b.y <= scanY &&
                             a.y > scanY);

                        if (!cross)
                            continue;

                        float t =
                            (scanY - a.y) /
                            (b.y - a.y);

                        intersections.Add(
                            new ScanIntersection(
                                a.x +
                                (b.x - a.x) *
                                t,
                                a.y < b.y
                                    ? 1
                                    : -1));
                    }
                }

                intersections.Sort(
                    (a, b) =>
                        a.x.CompareTo(b.x));

                if (style.fillRule ==
                    FillRule.EvenOdd)
                {
                    for (int i = 0;
                         i + 1 < intersections.Count;
                         i += 2)
                    {
                        FillSpan(
                            y,
                            intersections[i].x,
                            intersections[i + 1].x,
                            color);
                    }
                }
                else
                {
                    int winding = 0;
                    float spanStart = 0f;

                    foreach (ScanIntersection intersection
                             in intersections)
                    {
                        int previousWinding =
                            winding;

                        winding +=
                            intersection.winding;

                        if (previousWinding == 0 &&
                            winding != 0)
                        {
                            spanStart =
                                intersection.x;
                        }
                        else if (previousWinding != 0 &&
                                 winding == 0)
                        {
                            FillSpan(
                                y,
                                spanStart,
                                intersection.x,
                                color);
                        }
                    }
                }
            }
        }

        //codex-agent
        private readonly struct ScanIntersection
        {
            public readonly float x;
            public readonly int winding;

            public ScanIntersection(
                float x,
                int winding)
            {
                this.x = x;
                this.winding = winding;
            }
        }

        public void Stroke(
            List<Vector2> worldPath,
            Style style)
        {
            if (!style.strokeEnabled ||
                worldPath == null ||
                worldPath.Count < 2)
            {
                return;
            }

            Color color =
                style.strokeColor;

            color.a *=
                style.opacity *
                style.strokeOpacity;

            if (color.a <= 0.001f)
                return;

            float width =
                style.strokeWidth;

            for (int i = 0;
                 i < worldPath.Count - 1;
                 i++)
            {
                DrawLine(
                    WorldToPixel(
                        worldPath[i]),
                    WorldToPixel(
                        worldPath[i + 1]),
                    width,
                    color);
            }
        }

        private void DrawLine(
            Vector2 a,
            Vector2 b,
            float width,
            Color color)
        {
            float pixelWidth =
                width *
                GetPixelScale();

            float r =
                Mathf.Max(
                    0.5f,
                    pixelWidth *
                    0.5f);

            float minX =
                Mathf.Min(
                    a.x,
                    b.x) -
                r;

            float maxX =
                Mathf.Max(
                    a.x,
                    b.x) +
                r;

            float minY =
                Mathf.Min(
                    a.y,
                    b.y) -
                r;

            float maxY =
                Mathf.Max(
                    a.y,
                    b.y) +
                r;

            int x0 =
                Mathf.Clamp(
                    Mathf.FloorToInt(
                        minX),
                    0,
                    Width - 1);

            int x1 =
                Mathf.Clamp(
                    Mathf.CeilToInt(
                        maxX),
                    0,
                    Width - 1);

            int y0 =
                Mathf.Clamp(
                    Mathf.FloorToInt(
                        minY),
                    0,
                    Height - 1);

            int y1 =
                Mathf.Clamp(
                    Mathf.CeilToInt(
                        maxY),
                    0,
                    Height - 1);

            float r2 =
                r * r;

            Vector2 ab =
                b - a;

            float len2 =
                ab.sqrMagnitude;

            for (int y = y0;
                 y <= y1;
                 y++)
            {
                for (int x = x0;
                     x <= x1;
                     x++)
                {
                    Vector2 p =
                        new Vector2(
                            x + 0.5f,
                            y + 0.5f);

                    float t =
                        len2 >
                        0.000001f
                            ? Mathf.Clamp01(
                                Vector2.Dot(
                                    p - a,
                                    ab) /
                                len2)
                            : 0f;

                    Vector2 q =
                        a +
                        ab * t;

                    if ((p - q).sqrMagnitude <=
                        r2)
                    {
                        SetPixel(
                            x,
                            y,
                            color);
                    }
                }
            }
        }

        private void FillSpan(
            int y,
            float x0,
            float x1,
            Color color)
        {
            if (x1 < x0)
            {
                float t =
                    x0;

                x0 = x1;
                x1 = t;
            }

            int a =
                Mathf.Clamp(
                    Mathf.FloorToInt(
                        x0),
                    0,
                    Width - 1);

            int b =
                Mathf.Clamp(
                    Mathf.CeilToInt(
                        x1),
                    0,
                    Width - 1);

            for (int x = a;
                 x <= b;
                 x++)
            {
                SetPixel(
                    x,
                    y,
                    color);
            }
        }

        private void SetPixel(
            int x,
            int y,
            Color color)
        {
            if (x < 0 ||
                x >= Width ||
                y < 0 ||
                y >= Height)
            {
                return;
            }

            int index =
                y * Width + x;

            float sourceAlpha =
                Mathf.Clamp01(color.a);

            float destinationAlpha =
                alpha[index] / 255f;

            alpha[index] =
                (byte)Mathf.RoundToInt(
                    (sourceAlpha +
                     destinationAlpha *
                     (1f - sourceAlpha)) * 255f);
        }

        public void SetAlphaPixel(
            Vector2 world,
            Color color,
            float alpha)
        {
            Vector2 p =
                WorldToPixel(world);

            int x =
                Mathf.FloorToInt(
                    p.x);

            int y =
                Mathf.FloorToInt(
                    p.y);

            color.a *=
                alpha;

            SetPixel(
                x,
                y,
                color);
        }

        private Vector2 WorldToPixel(
            Vector2 p)
        {
            float x =
                (p.x - worldMinX) /
                worldWidth *
                (Width - 1);

            float y =
                (1f -
                 (p.y - worldMinY) /
                 worldHeight) *
                (Height - 1);

            return new Vector2(
                x,
                y);
        }

        public float GetPixelScale()
        {
            return Mathf.Min(
                Width /
                worldWidth,
                Height /
                worldHeight);
        }
    }

    // ============================================================
    // STYLE
    // ============================================================

    private sealed class Style
    {
        public bool fillEnabled = true;
        public Color fillColor = Color.white;
        public float fillOpacity = 1f;
        public FillRule fillRule = FillRule.NonZero;

        public bool strokeEnabled = false;
        public Color strokeColor = Color.black;
        public float strokeOpacity = 1f;
        public float strokeWidth = 1f;

        public float opacity = 1f;

        public static Style Read(
            XElement e,
            Style parent)
        {
            Style s =
                parent != null
                    ? parent.Clone()
                    : new Style();

            string fill =
                Attr(
                    e,
                    "fill");

            if (!string.IsNullOrEmpty(fill))
            {
                if (fill.Trim()
                        .ToLowerInvariant() ==
                    "none")
                {
                    s.fillEnabled =
                        false;
                }
                else
                {
                    s.fillEnabled =
                        true;

                    s.fillColor =
                        ParseColor(
                            fill,
                            Color.white);
                }
            }

            string stroke =
                Attr(
                    e,
                    "stroke");

            if (!string.IsNullOrEmpty(stroke))
            {
                if (stroke.Trim()
                        .ToLowerInvariant() ==
                    "none")
                {
                    s.strokeEnabled =
                        false;
                }
                else
                {
                    s.strokeEnabled =
                        true;

                    s.strokeColor =
                        ParseColor(
                            stroke,
                            Color.black);
                }
            }

            string sw =
                Attr(
                    e,
                    "stroke-width");

            if (!string.IsNullOrEmpty(sw))
            {
                s.strokeWidth =
                    F(sw);
            }

            string opacity =
                Attr(
                    e,
                    "opacity");

            if (!string.IsNullOrEmpty(opacity))
            {
                s.opacity =
                    Mathf.Clamp01(
                        F(opacity));
            }

            string fo =
                Attr(
                    e,
                    "fill-opacity");

            if (!string.IsNullOrEmpty(fo))
            {
                s.fillOpacity =
                    Mathf.Clamp01(
                        F(fo));
            }

            string fillRule =
                Attr(
                    e,
                    "fill-rule");

            if (!string.IsNullOrEmpty(fillRule))
            {
                s.fillRule =
                    ParseFillRule(fillRule);
            }

            string so =
                Attr(
                    e,
                    "stroke-opacity");

            if (!string.IsNullOrEmpty(so))
            {
                s.strokeOpacity =
                    Mathf.Clamp01(
                        F(so));
            }

            string style =
                Attr(
                    e,
                    "style");

            if (!string.IsNullOrEmpty(style))
            {
                ApplyCSS(
                    s,
                    style);
            }

            return s;
        }

        private static void ApplyCSS(
            Style s,
            string text)
        {
            string[] items =
                text.Split(';');

            foreach (string item
                     in items)
            {
                string[] p =
                    item.Split(':');

                if (p.Length != 2)
                    continue;

                string key =
                    p[0].Trim();

                string value =
                    p[1].Trim();

                if (key == "fill")
                {
                    if (value == "none")
                    {
                        s.fillEnabled =
                            false;
                    }
                    else
                    {
                        s.fillEnabled =
                            true;

                        s.fillColor =
                            ParseColor(
                                value,
                                Color.white);
                    }
                }
                else if (key == "stroke")
                {
                    if (value == "none")
                    {
                        s.strokeEnabled =
                            false;
                    }
                    else
                    {
                        s.strokeEnabled =
                            true;

                        s.strokeColor =
                            ParseColor(
                                value,
                                Color.black);
                    }
                }
                else if (key == "stroke-width")
                {
                    s.strokeWidth =
                        F(value);
                }
                else if (key == "opacity")
                {
                    s.opacity =
                        Mathf.Clamp01(
                            F(value));
                }
                else if (key == "fill-opacity")
                {
                    s.fillOpacity =
                        Mathf.Clamp01(
                            F(value));
                }
                else if (key == "fill-rule")
                {
                    s.fillRule =
                        ParseFillRule(value);
                }
                else if (key == "stroke-opacity")
                {
                    s.strokeOpacity =
                        Mathf.Clamp01(
                            F(value));
                }
            }
        }

        private Style Clone()
        {
            return new Style
            {
                fillEnabled =
                    fillEnabled,

                fillColor =
                    fillColor,

                fillOpacity =
                    fillOpacity,

                fillRule =
                    fillRule,

                strokeEnabled =
                    strokeEnabled,

                strokeColor =
                    strokeColor,

                strokeOpacity =
                    strokeOpacity,

                strokeWidth =
                    strokeWidth,

                opacity =
                    opacity
            };
        }

        //codex-agent
        private static FillRule ParseFillRule(
            string value)
        {
            return string.Equals(
                value?.Trim(),
                "evenodd",
                StringComparison.OrdinalIgnoreCase)
                ? FillRule.EvenOdd
                : FillRule.NonZero;
        }
    }

    //codex-agent
    private enum FillRule
    {
        NonZero,
        EvenOdd
    }

    // ============================================================
    // TRANSFORMS
    // ============================================================

    private static Matrix3x3 ParseTransform(
        string text)
    {
        if (string.IsNullOrEmpty(text))
            return Matrix3x3.identity;

        Matrix3x3 result =
            Matrix3x3.identity;

        MatchCollection matches =
            Regex.Matches(
                text,
                @"([a-zA-Z]+)\s*\(([^)]*)\)");

        foreach (Match m in matches)
        {
            string name =
                m.Groups[1].Value;

            string[] values =
                Regex.Split(
                    m.Groups[2].Value.Trim(),
                    @"[\s,]+");

            float[] v =
                new float[
                    values.Length];

            for (int i = 0;
                 i < values.Length;
                 i++)
            {
                v[i] =
                    F(values[i]);
            }

            Matrix3x3 t =
                Matrix3x3.identity;

            if (name == "translate")
            {
                float x =
                    v.Length > 0
                        ? v[0]
                        : 0f;

                float y =
                    v.Length > 1
                        ? v[1]
                        : 0f;

                t =
                    Matrix3x3.Translate(
                        new Vector2(
                            x,
                            y));
            }
            else if (name == "scale")
            {
                float x =
                    v.Length > 0
                        ? v[0]
                        : 1f;

                float y =
                    v.Length > 1
                        ? v[1]
                        : x;

                t =
                    Matrix3x3.Scale(
                        x,
                        y);
            }
            else if (name == "rotate")
            {
                float a =
                    v.Length > 0
                        ? v[0]
                        : 0f;

                if (v.Length >= 3)
                {
                    Matrix3x3 a1 =
                        Matrix3x3.Translate(
                            new Vector2(
                                v[1],
                                v[2]));

                    Matrix3x3 r =
                        Matrix3x3.Rotate(
                            a);

                    Matrix3x3 a2 =
                        Matrix3x3.Translate(
                            new Vector2(
                                -v[1],
                                -v[2]));

                    t =
                        a1 *
                        r *
                        a2;
                }
                else
                {
                    t =
                        Matrix3x3.Rotate(
                            a);
                }
            }
            else if (name == "skewX")
            {
                t =
                    Matrix3x3.SkewX(
                        v.Length > 0
                            ? v[0]
                            : 0f);
            }
            else if (name == "skewY")
            {
                t =
                    Matrix3x3.SkewY(
                        v.Length > 0
                            ? v[0]
                            : 0f);
            }
            else if (
                name == "matrix" &&
                v.Length >= 6)
            {
                t =
                    new Matrix3x3(
                        v[0],
                        v[2],
                        v[4],
                        v[1],
                        v[3],
                        v[5],
                        0f,
                        0f,
                        1f);
            }

            result =
                result * t;
        }

        return result;
    }

    private struct Matrix3x3
    {
        public float m00, m01, m02;
        public float m10, m11, m12;
        public float m20, m21, m22;

        public static Matrix3x3 identity =>
            new Matrix3x3(
                1f, 0f, 0f,
                0f, 1f, 0f,
                0f, 0f, 1f);

        public Matrix3x3(
            float a, float b, float c,
            float d, float e, float f,
            float g, float h, float i)
        {
            m00 = a;
            m01 = b;
            m02 = c;

            m10 = d;
            m11 = e;
            m12 = f;

            m20 = g;
            m21 = h;
            m22 = i;
        }

        public Vector2 MultiplyPoint(
            Vector2 p)
        {
            return new Vector2(
                m00 * p.x +
                m01 * p.y +
                m02,

                m10 * p.x +
                m11 * p.y +
                m12);
        }

        public static Matrix3x3 operator *(
            Matrix3x3 a,
            Matrix3x3 b)
        {
            return new Matrix3x3(
                a.m00 * b.m00 +
                a.m01 * b.m10 +
                a.m02 * b.m20,

                a.m00 * b.m01 +
                a.m01 * b.m11 +
                a.m02 * b.m21,

                a.m00 * b.m02 +
                a.m01 * b.m12 +
                a.m02 * b.m22,

                a.m10 * b.m00 +
                a.m11 * b.m10 +
                a.m12 * b.m20,

                a.m10 * b.m01 +
                a.m11 * b.m11 +
                a.m12 * b.m21,

                a.m10 * b.m02 +
                a.m11 * b.m12 +
                a.m12 * b.m22,

                a.m20 * b.m00 +
                a.m21 * b.m10 +
                a.m22 * b.m20,

                a.m20 * b.m01 +
                a.m21 * b.m11 +
                a.m22 * b.m21,

                a.m20 * b.m02 +
                a.m21 * b.m12 +
                a.m22 * b.m22);
        }

        public static Matrix3x3 Translate(
            Vector2 p)
        {
            return new Matrix3x3(
                1f, 0f, p.x,
                0f, 1f, p.y,
                0f, 0f, 1f);
        }

        public static Matrix3x3 Scale(
            float x,
            float y)
        {
            return new Matrix3x3(
                x, 0f, 0f,
                0f, y, 0f,
                0f, 0f, 1f);
        }

        public static Matrix3x3 Rotate(
            float degrees)
        {
            float r =
                degrees *
                Mathf.Deg2Rad;

            float c =
                Mathf.Cos(r);

            float s =
                Mathf.Sin(r);

            return new Matrix3x3(
                c, -s, 0f,
                s, c, 0f,
                0f, 0f, 1f);
        }

        public static Matrix3x3 SkewX(
            float degrees)
        {
            return new Matrix3x3(
                1f,
                Mathf.Tan(
                    degrees *
                    Mathf.Deg2Rad),
                0f,

                0f, 1f, 0f,
                0f, 0f, 1f);
        }

        public static Matrix3x3 SkewY(
            float degrees)
        {
            return new Matrix3x3(
                1f, 0f, 0f,

                Mathf.Tan(
                    degrees *
                    Mathf.Deg2Rad),
                1f, 0f,

                0f, 0f, 1f);
        }
    }

    // ============================================================
    // SHAPES
    // ============================================================

    //codex-agent
    private static int ArcSegments(
        float radius,
        float pixelsPerUnit)
    {
        return Mathf.Clamp(
            Mathf.CeilToInt(
                Mathf.Max(1f, radius * pixelsPerUnit) *
                Mathf.PI *
                0.5f),
            16,
            512);
    }

    private static List<Vector2> RoundedRect(
        float x,
        float y,
        float width,
        float height,
        float rx,
        float ry,
        int cornerSegments)
    {
        List<Vector2> result =
            new List<Vector2>();

        rx =
            Mathf.Clamp(
                Mathf.Abs(rx),
                0f,
                width * 0.5f);

        ry =
            Mathf.Clamp(
                Mathf.Abs(ry),
                0f,
                height * 0.5f);

        // Обычный прямоугольник.
        if (rx <= 0.0001f &&
            ry <= 0.0001f)
        {
            result.Add(
                new Vector2(
                    x,
                    y));

            result.Add(
                new Vector2(
                    x + width,
                    y));

            result.Add(
                new Vector2(
                    x + width,
                    y + height));

            result.Add(
                new Vector2(
                    x,
                    y + height));

            result.Add(
                new Vector2(
                    x,
                    y));

            return result;
        }

        /*
         * ВАЖНО:
         *
         * Здесь есть ВСЕ четыре прямые стороны.
         * Предыдущая версия содержала только 4 дуги,
         * из-за чего верхняя часть rounded rect отсутствовала.
         */

        // Начало верхней стороны.
        result.Add(
            new Vector2(
                x + rx,
                y));

        // Верхняя сторона.
        result.Add(
            new Vector2(
                x + width - rx,
                y));

        // Верхний правый угол.
        AddCorner(
            result,
            x + width - rx,
            y + ry,
            rx,
            ry,
            -90f,
            0f,
            cornerSegments);

        // Правая сторона.
        result.Add(
            new Vector2(
                x + width,
                y + height - ry));

        // Нижний правый угол.
        AddCorner(
            result,
            x + width - rx,
            y + height - ry,
            rx,
            ry,
            0f,
            90f,
            cornerSegments);

        // Нижняя сторона.
        result.Add(
            new Vector2(
                x + rx,
                y + height));

        // Нижний левый угол.
        AddCorner(
            result,
            x + rx,
            y + height - ry,
            rx,
            ry,
            90f,
            180f,
            cornerSegments);

        // Левая сторона.
        result.Add(
            new Vector2(
                x,
                y + ry));

        // Верхний левый угол.
        AddCorner(
            result,
            x + rx,
            y + ry,
            rx,
            ry,
            180f,
            270f,
            cornerSegments);

        // Явное замыкание.
        result.Add(
            result[0]);

        return result;
    }

    private static void AddCorner(
        List<Vector2> result,
        float cx,
        float cy,
        float rx,
        float ry,
        float startDegrees,
        float endDegrees,
        int segments)
    {
        /*
         * Начальную точку не добавляем повторно,
         * потому что она уже является концом прямой.
         */

        for (int i = 1;
             i <= segments;
             i++)
        {
            float t =
                i /
                (float)segments;

            float angle =
                Mathf.Lerp(
                    startDegrees,
                    endDegrees,
                    t) *
                Mathf.Deg2Rad;

            result.Add(
                new Vector2(
                    cx +
                    Mathf.Cos(angle) *
                    rx,

                    cy +
                    Mathf.Sin(angle) *
                    ry));
        }
    }

    private static List<Vector2> Ellipse(
        float cx,
        float cy,
        float rx,
        float ry,
        int segments)
    {
        List<Vector2> p =
            new List<Vector2>();

        for (int i = 0;
             i < segments;
             i++)
        {
            float a =
                Mathf.PI *
                2f *
                i /
                segments;

            p.Add(
                new Vector2(
                    cx +
                    Mathf.Cos(a) *
                    rx,

                    cy +
                    Mathf.Sin(a) *
                    ry));
        }

        p.Add(
            p[0]);

        return p;
    }

    private static List<Vector2> ParsePoints(
        string text)
    {
        List<Vector2> result =
            new List<Vector2>();

        if (string.IsNullOrEmpty(text))
            return result;

        string[] v =
            Regex.Split(
                text.Trim(),
                @"[\s,]+");

        for (int i = 0;
             i + 1 < v.Length;
             i += 2)
        {
            result.Add(
                new Vector2(
                    F(v[i]),
                    F(v[i + 1])));
        }

        return result;
    }

    private static void EnsurePath(
        ref List<Vector2> path,
        List<List<Vector2>> result,
        Vector2 current)
    {
        if (path == null)
        {
            path =
                new List<Vector2>();

            path.Add(
                current);

            result.Add(
                path);
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private static bool IsCommand(
        string s)
    {
        return
            !string.IsNullOrEmpty(s) &&
            s.Length == 1 &&
            char.IsLetter(s[0]);
    }

    private static int ParamCount(
        char c)
    {
        switch (
            char.ToUpper(c))
        {
            case 'M':
            case 'L':
                return 2;

            case 'H':
            case 'V':
                return 1;

            case 'C':
                return 6;

            case 'S':
            case 'Q':
                return 4;

            case 'T':
                return 2;

            case 'A':
                return 7;

            case 'Z':
                return 0;
        }

        return 0;
    }

    private static string Attr(
        XElement e,
        string name)
    {
        XAttribute a =
            e.Attribute(name);

        return a != null
            ? a.Value
            : null;
    }

    private static float Num(
        XElement e,
        string name)
    {
        string v =
            Attr(
                e,
                name);

        return
            string.IsNullOrEmpty(v)
                ? 0f
                : F(v);
    }

    private static float ReadSize(
        XElement e,
        string name,
        float fallback)
    {
        string s =
            Attr(
                e,
                name);

        if (string.IsNullOrEmpty(s))
            return fallback;

        s =
            Regex.Replace(
                s,
                @"[a-zA-Z%]+",
                "");

        float v =
            F(s);

        return
            v > 0f
                ? v
                : fallback;
    }

    private static float F(
        string s)
    {
        if (string.IsNullOrEmpty(s))
            return 0f;

        float.TryParse(
            s,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float value);

        return value;
    }

    private static Color ParseColor(
        string value,
        Color fallback)
    {
        if (string.IsNullOrEmpty(value))
            return fallback;

        value =
            value.Trim()
                 .ToLowerInvariant();

        if (value == "white")
            return Color.white;

        if (value == "black")
            return Color.black;

        if (value == "red")
            return Color.red;

        if (value == "green")
            return Color.green;

        if (value == "blue")
            return Color.blue;

        if (value == "transparent")
        {
            return new Color(
                0f,
                0f,
                0f,
                0f);
        }

        if (value.StartsWith("#"))
        {
            string hex =
                value.Substring(1);

            if (hex.Length == 3)
            {
                hex =
                    "" +
                    hex[0] + hex[0] +
                    hex[1] + hex[1] +
                    hex[2] + hex[2];
            }

            if (hex.Length == 6)
                hex += "FF";

            if (hex.Length == 8 &&
                uint.TryParse(
                    hex,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out uint n))
            {
                return new Color(
                    ((n >> 24) & 255) /
                    255f,

                    ((n >> 16) & 255) /
                    255f,

                    ((n >> 8) & 255) /
                    255f,

                    (n & 255) /
                    255f);
            }
        }

        Match rgb =
            Regex.Match(
                value,
                @"rgba?\(([^)]+)\)");

        if (rgb.Success)
        {
            string[] p =
                Regex.Split(
                    rgb.Groups[1].Value,
                    @"[\s,]+");

            if (p.Length >= 3)
            {
                float r =
                    F(p[0]) /
                    255f;

                float g =
                    F(p[1]) /
                    255f;

                float b =
                    F(p[2]) /
                    255f;

                float a =
                    p.Length > 3
                        ? F(p[3])
                        : 1f;

                return new Color(
                    r,
                    g,
                    b,
                    a);
            }
        }

        return fallback;
    }

    private static void Transform(
        List<Vector2> points,
        Matrix3x3 matrix)
    {
        for (int i = 0;
             i < points.Count;
             i++)
        {
            points[i] =
                matrix.MultiplyPoint(
                    points[i]);
        }
    }

    private static bool Approximately(
        Vector2 a,
        Vector2 b)
    {
        return
            (a - b).sqrMagnitude <
            0.0001f;
    }
}
