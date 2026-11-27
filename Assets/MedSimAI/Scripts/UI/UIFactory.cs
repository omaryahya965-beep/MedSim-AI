using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MedSimAI.UI
{
    /// <summary>MedSim AI visual identity + helpers for world-space uGUI built in code.</summary>
    public static class Brand
    {
        public static readonly Color Navy = new Color32(10, 25, 47, 245);
        public static readonly Color NavyLight = new Color32(23, 42, 69, 255);
        public static readonly Color Teal = new Color32(0, 168, 150, 255);
        public static readonly Color TealLight = new Color32(100, 255, 218, 255);
        public static readonly Color Blue = new Color32(66, 165, 245, 255);
        public static readonly Color White = new Color32(236, 242, 248, 255);
        public static readonly Color Muted = new Color32(136, 146, 176, 255);
        public static readonly Color Warn = new Color32(255, 183, 77, 255);
        public static readonly Color Danger = new Color32(255, 138, 128, 255);
    }

    public static class UIFactory
    {
        public static Func<Canvas, Component> RaycasterFactory; // set by XR layer (TrackedDeviceGraphicRaycaster)

        /// <summary>World-space canvas: size in canvas units (1 unit = 1 mm at default scale 0.001).</summary>
        public static Canvas WorldCanvas(string name, Transform parent, Vector2 sizeMm, float scale = 0.001f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform; rt.sizeDelta = sizeMm; rt.localScale = Vector3.one * scale;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            if (RaycasterFactory != null) RaycasterFactory(c); else go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Panel(Transform parent, string name, Color color, Vector2? size = null, bool rounded = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.color = color;
            if (rounded) { img.sprite = RoundedSprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.5f; }
            var rt = (RectTransform)go.transform;
            if (size.HasValue) rt.sizeDelta = size.Value; else Stretch(rt);
            return rt;
        }

        public static void Stretch(RectTransform rt, float pad = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        }

        public static TMP_Text Text(Transform parent, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style; t.textWrappingMode = TextWrappingModes.Normal; t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string label, Action onClick, Color? bg = null, float fontSize = 22, float height = 64)
        {
            var rt = Panel(parent, "Btn " + label, bg ?? Brand.NavyLight, new Vector2(300, height));
            var le = rt.gameObject.AddComponent<LayoutElement>(); le.minHeight = height; le.preferredHeight = height; le.flexibleWidth = 1;
            var b = rt.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(0.75f, 1f, 0.97f); colors.pressedColor = new Color(0.5f, 0.9f, 0.85f); colors.fadeDuration = 0.08f; b.colors = colors;
            b.onClick.AddListener(() => onClick?.Invoke());
            var t = Text(rt, label, fontSize, Brand.White, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(t.rectTransform, 10);
            return b;
        }

        public static RectTransform VStack(Transform parent, string name, float spacing = 10, int pad = 16)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<VerticalLayoutGroup>(); v.spacing = spacing; v.padding = new RectOffset(pad, pad, pad, pad);
            v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            return (RectTransform)go.transform;
        }

        public static RectTransform Grid(Transform parent, string name, Vector2 cell, float spacing = 10)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<GridLayoutGroup>(); g.cellSize = cell; g.spacing = Vector2.one * spacing; g.padding = new RectOffset(12, 12, 12, 12);
            return (RectTransform)go.transform;
        }

        public static RectTransform ScrollArea(Transform parent, out RectTransform content)
        {
            var root = Panel(parent, "Scroll", new Color(0, 0, 0, 0.12f));
            var sr = root.gameObject.AddComponent<ScrollRect>(); sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30;
            var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)); vp.transform.SetParent(root, false); Stretch((RectTransform)vp.transform, 4);
            var img = vp.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0.001f);
            content = VStack(vp.transform, "Content", 8, 8);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = (RectTransform)vp.transform; sr.content = content;
            return root;
        }

        public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }

        static Sprite rounded;
        public static Sprite RoundedSprite
        {
            get
            {
                if (rounded) return rounded;
                int s = 64, r = 16; var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(r - x, x - (s - 1 - r))), dy = Mathf.Max(0, Mathf.Max(r - y, y - (s - 1 - r)));
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                tex.Apply();
                rounded = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                return rounded;
            }
        }
    }
}
