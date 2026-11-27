using System.Linq;
using System.Text;
using MedSimAI.AI;
using MedSimAI.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Reception directory (world-space, readable in VR and on desktop): the two training rooms, the training mode, the
    /// language, today's sessions and the clinical-validation notice. VR: point and click. Desktop: keys 1 / 2 / 3 and F2.
    /// Arabic is shaped right-to-left with the project's ArabicShaper, in the same style as the existing menus.
    /// </summary>
    public class HospitalDirectory : MonoBehaviour
    {
        public HospitalCoordinator coordinator;
        public Vector2 sizeMm = new Vector2(900, 1180);
        TMP_Text title, room01, room02, mode, lang, status, summary, notice, keysHint;
        bool built;

        void Start() { Build(); Refresh(); }

        void Build()
        {
            if (built) return; built = true;
            var canvas = UIFactory.WorldCanvas("Hospital Directory Canvas", transform, sizeMm);
            var bg = UIFactory.Panel(canvas.transform, "Directory", Brand.Navy); UIFactory.Stretch(bg);
            var col = UIFactory.VStack(bg, "Column", 18, 36);
            title = UIFactory.Text(col, "", 46, Brand.White, TextAlignmentOptions.Center, FontStyles.Bold);
            room01 = Button(col, () => coordinator.RequestRoom(HospitalRoomId.Room01, "directory"), Brand.Teal);
            room02 = Button(col, () => coordinator.RequestRoom(HospitalRoomId.Room02, "directory"), Brand.Teal);
            mode = Button(col, () => coordinator.SetGuidedMode(!coordinator.guidedMode), Brand.NavyLight);
            lang = Button(col, () => { var ai = AIProviderManager.Instance; if (ai) ai.SetLanguageMode(coordinator.Language == "ar" ? LanguageMode.English : LanguageMode.Arabic); Refresh(); }, Brand.NavyLight);
            status = UIFactory.Text(col, "", 28, Brand.TealLight, TextAlignmentOptions.Center);
            summary = UIFactory.Text(col, "", 24, Brand.White, TextAlignmentOptions.TopLeft);
            keysHint = UIFactory.Text(col, "", 22, Brand.Muted, TextAlignmentOptions.Center);
            notice = UIFactory.Text(col, "", 22, Brand.Warn, TextAlignmentOptions.Center, FontStyles.Bold);
        }

        static TMP_Text Button(Transform parent, System.Action onClick, Color bg)
        {
            var b = UIFactory.Button(parent, " ", onClick, bg, 30, 96);
            return b.GetComponentInChildren<TMP_Text>();
        }

        static void Set(TMP_Text t, string text) { if (t) ArabicShaper.Apply(t, text); }

        public void Refresh()
        {
            if (!built || !coordinator) return;
            bool ar = coordinator.Language == "ar";
            Set(title, ar ? "مستشفى ميدسيم للتدريب" : "MedSim AI Training Hospital");
            Set(room01, ar ? "١  الغرفة 01 — الفحص السريري" : "1  Room 01 — Clinical Examination");
            Set(room02, ar ? "٢  الغرفة 02 — التدريب الجراحي" : "2  Room 02 — Surgical Training");
            Set(mode, coordinator.guidedMode ? (ar ? "الوضع: تدريب موجّه (F2)" : "Mode: Guided training (F2)") : (ar ? "الوضع: تقييم مستقل (F2)" : "Mode: Independent assessment (F2)"));
            Set(lang, ar ? "Language: English" : "اللغة: العربية");
            string where = coordinator.GuidingTo != HospitalRoomId.None
                ? (ar ? "الدكتورة سارة عم تمشي معك إلى " : "Dr. Hassan is walking you to ") + RoomName(coordinator.GuidingTo, ar)
                : (ar ? "أنت الآن في: " : "You are in: ") + RoomName(coordinator.PlayerRoom, ar);
            Set(status, where);
            var sb = new StringBuilder(ar ? "جلسات اليوم:\n" : "Today's sessions:\n");
            if (coordinator.Records.Count == 0) sb.Append(ar ? "لا يوجد بعد" : "none yet");
            foreach (var r in coordinator.Records.Skip(System.Math.Max(0, coordinator.Records.Count - 4)))
                sb.Append($"• {RoomName(r.room, ar)} — {(r.outcome == "completed" ? (ar ? "مكتملة" : "completed") : (ar ? "لم تكتمل" : "not finished"))}{(r.scorePercent >= 0 ? $" — {r.scorePercent}%" : "")} — {r.Minutes:0.0} {(ar ? "دقيقة" : "min")}\n");
            Set(summary, sb.ToString().TrimEnd());
            Set(keysHint, ar ? "اضغط 1 أو 2 أو 3 (الممر) · أو احكي مع الدكتورة (T)" : "Press 1, 2 or 3 (corridor) · or talk to the doctor (T)");
            Set(notice, ar ? "مسودة — غير معتمدة سريرياً · ليست بديلاً عن الإشراف السريري ولا شهادة كفاءة" : "DRAFT — NOT CLINICALLY VALIDATED · not a substitute for clinical supervision or a competency certificate");
        }

        static string RoomName(HospitalRoomId r, bool ar) => r == HospitalRoomId.Room01 ? (ar ? "الغرفة 01" : "Room 01") : r == HospitalRoomId.Room02 ? (ar ? "الغرفة 02 (العمليات)" : "Room 02 (operating room)") : (ar ? "الممر" : "the corridor");
    }
}
