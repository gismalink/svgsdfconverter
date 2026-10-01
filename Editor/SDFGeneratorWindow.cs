using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class SDFGeneratorWindow : EditorWindow
{
    //codex-agent
    [SerializeField]
    private List<DefaultAsset> svgAssets =
        new List<DefaultAsset>();

    [SerializeField]
    private int rasterResolution = 2048;

    [SerializeField]
    private int paddingPixels = 32;

    [SerializeField]
    private float maxDistance = 32f;

    private Texture2D previewTexture;

    [MenuItem("Tools/SVG SDF Generator")]
    public static void ShowWindow()
    {
        SDFGeneratorWindow window =
            GetWindow<SDFGeneratorWindow>("SVG → SDF");

        window.minSize = new Vector2(460f, 460f);
    }

    //codex-agent
    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("SVG → SDF", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Add SVG files to the list or drag them here. Each SDF PNG is saved next to its source SVG.",
            MessageType.Info);

        DrawSourceList();

        EditorGUILayout.Space(8);
        rasterResolution = EditorGUILayout.IntPopup(
            "Raster Resolution", rasterResolution,
            new[] { "512", "1024", "2048", "4096" },
            new[] { 512, 1024, 2048, 4096 });
        paddingPixels = EditorGUILayout.IntSlider(
            "Padding (px)",
            paddingPixels,
            0,
            Mathf.Max(0, rasterResolution / 4));
        maxDistance = EditorGUILayout.FloatField("Max Distance", maxDistance);
        int validSvgCount = GetValidSvgCount();
        GUI.enabled = validSvgCount > 0;
        if (GUILayout.Button(
                "Generate " + validSvgCount + " SDF texture(s)",
                GUILayout.Height(42)))
        {
            GenerateAll();
        }
        GUI.enabled = true;

        DrawPreview();
    }

    //codex-agent
    private void DrawSourceList()
    {
        EditorGUILayout.LabelField("Source SVGs", EditorStyles.boldLabel);

        Rect dropArea = GUILayoutUtility.GetRect(
            0f, 48f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag SVG files here");
        HandleDragAndDrop(dropArea);

        for (int i = 0; i < svgAssets.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            svgAssets[i] = (DefaultAsset)EditorGUILayout.ObjectField(
                svgAssets[i], typeof(DefaultAsset), false);

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);

            if (GUILayout.Button("−", GUILayout.Width(28f)))
            {
                svgAssets.RemoveAt(i);
                EditorUtility.SetDirty(this);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (svgAssets[i] != null && !IsValidSvg(svgAssets[i]))
            {
                EditorGUILayout.HelpBox(
                    "Only .svg files can be generated.", MessageType.Warning);
            }
        }

        if (GUILayout.Button("Add SVG"))
        {
            svgAssets.Add(null);
            EditorUtility.SetDirty(this);
        }
    }

    //codex-agent
    private void HandleDragAndDrop(Rect dropArea)
    {
        Event currentEvent = Event.current;
        if (!dropArea.Contains(currentEvent.mousePosition))
            return;

        if (currentEvent.type != EventType.DragUpdated &&
            currentEvent.type != EventType.DragPerform)
        {
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (currentEvent.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (UnityEngine.Object item in DragAndDrop.objectReferences)
            {
                DefaultAsset svg = item as DefaultAsset;
                if (IsValidSvg(svg) && !svgAssets.Contains(svg))
                {
                    svgAssets.Add(svg);
                    EditorUtility.SetDirty(this);
                }
            }
        }
        currentEvent.Use();
    }

    //codex-agent
    private int GetValidSvgCount()
    {
        int count = 0;
        foreach (DefaultAsset svg in svgAssets)
        {
            if (IsValidSvg(svg))
                count++;
        }
        return count;
    }

    //codex-agent
    private static bool IsValidSvg(DefaultAsset svg)
    {
        if (svg == null)
            return false;

        string path = AssetDatabase.GetAssetPath(svg);
        return !string.IsNullOrEmpty(path) && path.EndsWith(
            ".svg", StringComparison.OrdinalIgnoreCase);
    }

    //codex-agent
    private void GenerateAll()
    {
        List<DefaultAsset> sources = new List<DefaultAsset>();
        foreach (DefaultAsset svg in svgAssets)
        {
            if (IsValidSvg(svg) && !sources.Contains(svg))
                sources.Add(svg);
        }

        try
        {
            for (int i = 0; i < sources.Count; i++)
            {
                string svgPath = AssetDatabase.GetAssetPath(sources[i]);
                EditorUtility.DisplayProgressBar(
                    "SVG → SDF", "Generating " + Path.GetFileName(svgPath),
                    (float)i / sources.Count);
                previewTexture = GenerateOne(svgPath);
            }

            EditorUtility.DisplayDialog(
                "SVG → SDF",
                "Generated " + sources.Count +
                " SDF texture(s) next to their SVG source files.",
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "SVG → SDF Error", exception.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    //codex-agent
    private Texture2D GenerateOne(string svgPath)
    {
        Texture2D generated = SDFGenerator.GenerateFromSVG(
            svgPath,
            rasterResolution,
            maxDistance,
            paddingPixels);

        if (generated == null)
            throw new Exception("SDF generation returned null for " + svgPath);

        string outputPath = Path.Combine(
            Path.GetDirectoryName(svgPath),
            Path.GetFileNameWithoutExtension(svgPath) + "_SDF.png")
            .Replace("\\", "/");

        try
        {
            SDFGenerator.SavePNG(generated, outputPath);
            SDFGenerator.ConfigureImporter(outputPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }
        finally
        {
            DestroyImmediate(generated);
        }
    }

    //codex-agent
    private void DrawPreview()
    {
        if (previewTexture == null)
            return;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField(
            "Last Generated Texture", EditorStyles.boldLabel);
        float availableWidth = position.width - 30f;
        float aspect = previewTexture.width / (float)previewTexture.height;
        Rect rect = GUILayoutUtility.GetRect(
            availableWidth, availableWidth / aspect);
        EditorGUI.DrawPreviewTexture(
            rect, previewTexture, null, ScaleMode.ScaleToFit);
    }
}
