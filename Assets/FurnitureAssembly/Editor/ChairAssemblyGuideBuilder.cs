using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FurnitureAssembly.Editor
{
    public static class ChairAssemblyGuideBuilder
    {
        const string Folder = "Assets/FurnitureAssembly/UI";
        static readonly Color Ink = new Color(0.22f, 0.17f, 0.13f);
        static readonly Color Secondary = new Color(0.39f, 0.34f, 0.28f);
        static readonly Color Olive = new Color(0.29f, 0.40f, 0.27f);

        [MenuItem("Tools/Furniture Assembly/Create Assembly Guide")]
        public static void CreateGuide()
        {
            if (EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Create the guide in Edit Mode.");
            var assembly = Object.FindFirstObjectByType<ChairAssemblyController>();
            if (assembly == null || GameObject.Find("Assembly Guide") != null)
                throw new System.InvalidOperationException("The scene needs a chair and no existing Assembly Guide.");

            var root = new GameObject("Assembly Guide");

            var canvasObject = new GameObject("Guide Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(1040f, 900f);
            canvasRect.localScale = Vector3.one * 0.001f;
            canvasRect.localPosition = new Vector3(0f, 0f, -0.01f);
            canvas.referencePixelsPerUnit = 100f;
            Fill("Warm paper", canvasRect, 0, 0, 1040, 900, new Color(0.94f, 0.91f, 0.83f));
            Fill("Top accent", canvasRect, 64, 34, 64, 7, Olive);

            var instructions = Rect("Instructions", canvasRect, 0, 0, 1040, 690).gameObject;
            Text("Title", instructions.transform, "Build your chair", 64, 65, 912, 75, 54, Ink, true);
            Fill("Divider", instructions.transform, 64, 153, 912, 2, new Color(0.76f, 0.72f, 0.63f));
            Instruction(instructions.transform, 190, "GRAB & ROTATE",
                "Hold Grip to pick up a piece.\nMove and turn your hand to position it.");
            Instruction(instructions.transform, 350, "ALIGN & RELEASE",
                "Match the wooden joints. When the\nmarkers turn green, release Grip to snap.");
            Instruction(instructions.transform, 510, "KEEP BUILDING",
                "Connect pieces in any order.\nJoined pieces can be picked up together.");

            var completion = Rect("Completion", canvasRect, 0, 0, 1040, 690).gameObject;
            Text("Completion title", completion.transform, "Chair complete!", 64, 245, 912, 90, 62, Olive, true);
            Text("Completion message", completion.transform,
                "All seven pieces are connected.\n\nPick up your chair and take a look.",
                64, 370, 912, 220, 34, Ink, false);
            completion.SetActive(false);

            Fill("Footer divider", canvasRect, 64, 714, 912, 2, new Color(0.76f, 0.72f, 0.63f));
            var label = Text("Connection progress", canvasRect, "CONNECTIONS  0 / 6", 64, 748, 912, 40, 26, Secondary, true);
            Fill("Progress track", canvasRect, 64, 810, 912, 12, new Color(0.80f, 0.77f, 0.69f));
            var progress = Fill("Progress fill", canvasRect, 64, 810, 912, 12, Olive);
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Horizontal;
            progress.fillAmount = 0f;
            var guide = root.AddComponent<ChairAssemblyGuide>();
            guide.Configure(null, instructions, completion, label, progress);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/AssemblyGuide.prefab");
            Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "Assembly Guide";
            instance.transform.position = new Vector3(assembly.transform.position.x - 0.8f, 1.35f,
                assembly.transform.position.z + 0.7f);
            var camera = GameObject.Find("XR Origin Hands (XR Rig)").GetComponentInChildren<Camera>(true);
            var direction = instance.transform.position - camera.transform.position;
            direction.y = 0f;
            instance.transform.rotation = Quaternion.LookRotation(direction);
            var binding = new SerializedObject(instance.GetComponent<ChairAssemblyGuide>());
            binding.FindProperty("assembly").objectReferenceValue = assembly;
            binding.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<ChairAssemblyGuide>());
            EditorSceneManager.MarkSceneDirty(instance.scene);
            EditorSceneManager.SaveScene(instance.scene);
            Selection.activeGameObject = instance;
        }

        static void Instruction(Transform parent, float top, string title, string body)
        {
            Text(title, parent, title, 64, top, 912, 44, 30, Olive, true);
            Text(title + " description", parent, body, 64, top + 52, 912, 86, 31, Ink, false);
        }

        static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static Image Fill(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            var image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text Text(string name, Transform parent, string content, float x, float y,
            float width, float height, float size, Color color, bool bold)
        {
            var text = Rect(name, parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            text.text = content;
            text.fontSize = size;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

    }
}
