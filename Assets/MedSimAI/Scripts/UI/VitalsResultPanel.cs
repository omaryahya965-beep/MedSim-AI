using System.Collections;
using TMPro;
using UnityEngine;

namespace MedSimAI.UI
{
    /// <summary>
    /// A clear, contextual result card for a measurement the student just took (blood pressure + pulse, SpO₂ + pulse):
    /// a world-space panel that appears between the student and the patient, at a comfortable reading distance and height,
    /// always facing the student (desktop and VR alike; sized for headset readability: the main value is ~6 cm tall at ~0.95 m).
    /// It fades out after a while; the bedside monitor keeps the value. Values come only from the simulation (authored vitals).
    /// </summary>
    public class VitalsResultPanel : MonoBehaviour
    {
        public static VitalsResultPanel Instance { get; private set; }
        public float showSeconds = 18f, distance = 0.95f;
        Canvas canvas; TMP_Text title, value, sub, footer; CanvasGroup group; float shownAt = -100f; Transform focus;
        public bool Visible => canvas && canvas.gameObject.activeSelf && group && group.alpha > 0.05f;
        public string Text => value ? title.text + " " + value.text + " " + sub.text : "";

        public static VitalsResultPanel Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("[Vitals result panel]").AddComponent<VitalsResultPanel>();
            return Instance;
        }

        void Build()
        {
            canvas = UIFactory.WorldCanvas("Vitals Result", transform, new Vector2(560, 330), 0.001f);
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            var bg = UIFactory.Panel(canvas.transform, "Card", new Color(0.02f, 0.07f, 0.12f, 0.94f)); UIFactory.Stretch(bg);
            var bar = UIFactory.Panel(bg, "Accent", Brand.Teal, null, false); UIFactory.Place(bar, new Vector2(0, 0.93f), new Vector2(1, 1));
            title = UIFactory.Text(bg, "", 30, Brand.TealLight, TextAlignmentOptions.Center, FontStyles.Bold); UIFactory.Place(title.rectTransform, new Vector2(0.03f, 0.74f), new Vector2(0.97f, 0.92f));
            value = UIFactory.Text(bg, "", 92, Brand.White, TextAlignmentOptions.Center, FontStyles.Bold); UIFactory.Place(value.rectTransform, new Vector2(0.03f, 0.36f), new Vector2(0.97f, 0.76f));
            sub = UIFactory.Text(bg, "", 40, new Color(0.55f, 1f, 0.6f), TextAlignmentOptions.Center, FontStyles.Bold); UIFactory.Place(sub.rectTransform, new Vector2(0.03f, 0.17f), new Vector2(0.97f, 0.37f));
            footer = UIFactory.Text(bg, "", 22, Brand.Muted, TextAlignmentOptions.Center); UIFactory.Place(footer.rectTransform, new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.17f));
            canvas.gameObject.SetActive(false);
        }

        /// <summary>Shows a result card near <paramref name="near"/> (e.g. the patient's arm), facing the student.</summary>
        public void Show(string heading, string main, string secondary, string where, Transform near)
        {
            if (!canvas) Build();
            ArabicShaper.Apply(title, heading); value.text = main; ArabicShaper.Apply(sub, secondary); ArabicShaper.Apply(footer, where);
            focus = near; shownAt = Time.time; canvas.gameObject.SetActive(true); group.alpha = 1f;
            Place(true);
        }

        void Place(bool snap)
        {
            var cam = Camera.main; if (!cam || !canvas) return;
            var head = cam.transform.position;
            // along the student's line of sight to what was measured (e.g. the arm), offset to the right so it never hides it
            var dir = focus ? focus.position - head : cam.transform.forward; if (dir.sqrMagnitude < 1e-4f) dir = cam.transform.forward;
            dir.Normalize();
            var flat = Vector3.ProjectOnPlane(dir, Vector3.up); if (flat.sqrMagnitude < 1e-4f) flat = cam.transform.forward; flat.Normalize();
            var side = Vector3.Cross(Vector3.up, flat);
            var p = head + dir * (distance * 0.85f) + side * 0.32f + Vector3.up * 0.06f;
            var t = canvas.transform;
            t.position = snap ? p : Vector3.Lerp(t.position, p, Time.deltaTime * 2f);
            t.rotation = Quaternion.LookRotation(t.position - head);
        }

        void LateUpdate()
        {
            if (!canvas || !canvas.gameObject.activeSelf) return;
            float age = Time.time - shownAt;
            if (age > showSeconds) { group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.deltaTime); if (group.alpha <= 0f) canvas.gameObject.SetActive(false); }
            Place(false);
        }

        public void Hide() { if (canvas) canvas.gameObject.SetActive(false); }
    }
}
