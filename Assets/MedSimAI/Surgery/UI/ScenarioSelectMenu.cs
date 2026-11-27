using MedSimAI.Surgery.Shared;
using MedSimAI.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MedSimAI.Surgery.UI
{
    /// <summary>
    /// Professional bilingual scenario-selection screen: Clinical Examination (existing chest-pain simulation) or
    /// Surgical Training (orthopaedic knee module). Mouse on desktop, controller rays in VR (existing XR UI raycaster).
    /// </summary>
    public class ScenarioSelectMenu : MonoBehaviour
    {
        Canvas canvas; RectTransform root; bool desktop;

        void Start()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            // No XRPlayerController in the menu scene, so register the XR UI raycaster for controller rays ourselves.
            if (UIFactory.RaycasterFactory == null) UIFactory.RaycasterFactory = c => c.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            desktop = SurgeryCanvas.IsDesktop;
            Build();
        }

        void Update()
        {
            bool d = SurgeryCanvas.IsDesktop;
            if (d != desktop) { desktop = d; Build(); }
            if (!desktop && canvas && Camera.main && Vector3.Angle(Camera.main.transform.forward, canvas.transform.position - Camera.main.transform.position) > 70) SurgeryCanvas.PlaceInFront(canvas, Camera.main, 1.6f, 0.05f);
        }

        string L(string en, string ar) => ScenarioRouter.Language == "ar" ? ar : en;

        TMP_Text Txt(Transform p, string s, float size, Color c, TextAlignmentOptions a = TextAlignmentOptions.TopLeft, FontStyles st = FontStyles.Normal)
        {
            var t = UIFactory.Text(p, "", size, c, a, st); ArabicShaper.Apply(t, s); return t;
        }

        void Build()
        {
            if (canvas) Destroy(canvas.gameObject);
            canvas = SurgeryCanvas.Create("Scenario Menu", new Vector2(1400, 820), desktop);
            if (!desktop) SurgeryCanvas.PlaceInFront(canvas, Camera.main, 1.6f, 0.05f);
            root = UIFactory.Panel(canvas.transform, "Root", Brand.Navy);
            if (desktop) UIFactory.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-700, -410), new Vector2(700, 410));

            var title = Txt(root, L("MedSim AI — Choose a training scenario", "MedSim AI — اختر سيناريو التدريب"), 44, Brand.White, TextAlignmentOptions.Center, FontStyles.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0, 0.86f), new Vector2(1, 0.97f));
            var sub = Txt(root, L("Educational simulations for medical students", "محاكاة تعليمية لطلاب الطب"), 24, Brand.Muted, TextAlignmentOptions.Center);
            UIFactory.Place(sub.rectTransform, new Vector2(0, 0.80f), new Vector2(1, 0.87f));

            Card(new Vector2(0.04f, 0.17f), new Vector2(0.49f, 0.77f),
                L("1 · Clinical Examination", "١ · الفحص السريري"),
                L("Emergency department: assess Mr. Adam Reyes (58) with chest pain. Talk to the patient and Dr. Sara Hassan by voice, use real instruments, and get a personal debrief.",
                  "قسم الطوارئ: افحص السيد آدم ريس (58 سنة) اللي عنده ألم بالصدر. احكي مع المريض ومع د. سارة حسن بالصوت، استخدم أدوات حقيقية، وخذ تقييم شخصي بالآخر."),
                "", ScenarioRouter.StartClinical, ScenarioRouter.InBuild(ScenarioRouter.ClinicalScene));
            Card(new Vector2(0.51f, 0.17f), new Vector2(0.96f, 0.77f),
                L("2 · Surgical Training", "٢ · التدريب الجراحي"),
                L("Operating room: guided, simplified total knee replacement (right knee) with an AI orthopaedic surgeon and a surgical assistant. Arabic and English voice.",
                  "غرفة العمليات: تدريب موجّه ومبسّط على تبديل مفصل الركبة اليمنى، مع جرّاحة عظام ذكية ومساعد عمليات. بالعربي والإنجليزي بالصوت."),
                L(SurgeryContract.VALIDATION_BANNER, SurgeryContract.VALIDATION_BANNER_AR), ScenarioRouter.StartSurgical, ScenarioRouter.InBuild(ScenarioRouter.SurgicalScene));

            var bar = new GameObject("Bar", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            bar.SetParent(root, false); UIFactory.Place(bar, new Vector2(0.25f, 0.03f), new Vector2(0.75f, 0.12f));
            var h = bar.GetComponent<HorizontalLayoutGroup>(); h.spacing = 16; h.childForceExpandWidth = true; h.childControlWidth = true; h.childControlHeight = true;
            UIFactory.Button(bar, ScenarioRouter.Language == "ar" ? "English" : "العربية", () => { ScenarioRouter.Language = ScenarioRouter.Language == "ar" ? "en" : "ar"; Build(); }, Brand.NavyLight, 24, 64);
            UIFactory.Button(bar, L("Quit", "خروج"), Application.Quit, Brand.NavyLight, 24, 64);
        }

        void Card(Vector2 min, Vector2 max, string title, string body, string banner, System.Action start, bool available)
        {
            var card = UIFactory.Panel(root, "Card " + title, Brand.NavyLight); UIFactory.Place(card, min, max);
            var t = Txt(card, title, 34, Brand.TealLight, TextAlignmentOptions.TopLeft, FontStyles.Bold); UIFactory.Place(t.rectTransform, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.95f));
            var b = Txt(card, body, 24, Brand.White); UIFactory.Place(b.rectTransform, new Vector2(0.05f, 0.36f), new Vector2(0.95f, 0.80f));
            if (!string.IsNullOrEmpty(banner)) { var w = Txt(card, "⚠ " + banner, 20, Brand.Warn, TextAlignmentOptions.TopLeft, FontStyles.Bold); UIFactory.Place(w.rectTransform, new Vector2(0.05f, 0.25f), new Vector2(0.95f, 0.35f)); }
            var btn = UIFactory.Button(card, available ? L("Start", "ابدأ") : L("Not in this build", "مش موجود بهالنسخة"), () => { if (available) start(); }, available ? Brand.Teal : Brand.Muted, 28, 72);
            UIFactory.Place((RectTransform)btn.transform, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.20f));
            btn.interactable = available;
            if (ScenarioRouter.Language == "ar") { t.alignment = TextAlignmentOptions.TopRight; b.alignment = TextAlignmentOptions.TopRight; }
        }
    }
}
