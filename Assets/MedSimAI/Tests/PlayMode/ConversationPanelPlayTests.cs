using System.Collections;
using System.Linq;
using System.Reflection;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.PlayTests
{
    /// <summary>v0.5 conversation panel in the VR (world-space) layout: long Arabic/English text stays inside its row, rows never overlap.</summary>
    public class ConversationPanelPlayTests
    {
        [UnitySetUp] public IEnumerator Load()
        {
            AIProviderManager.ForceOffline = true; MedSimAI.XR.DisplayModeManager.DisableAutoDesktop = true;
            yield return SceneManager.LoadSceneAsync("MedSim_ER");
            float t = 0; while ((SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
            yield return null;
        }

        static Rect WR(RectTransform r) { var c = new Vector3[4]; r.GetWorldCorners(c); var right = (c[3] - c[0]).normalized; var up = (c[1] - c[0]).normalized; float w = Vector3.Distance(c[0], c[3]), h = Vector3.Distance(c[0], c[1]); return new Rect(0, 0, w, h); }
        static bool Inside(TMPro.TMP_Text t)
        {
            t.ForceMeshUpdate(); var ti = t.textInfo; var r = t.rectTransform.rect; int page = t.pageToDisplay - 1;
            for (int i = 0; i < ti.characterCount; i++) { var ch = ti.characterInfo[i]; if (!ch.isVisible || ch.pageNumber != page) continue;
                if (ch.bottomLeft.x < r.xMin - 1 || ch.topRight.x > r.xMax + 1 || ch.bottomLeft.y < r.yMin - 2 || ch.topRight.y > r.yMax + 2) return false; }
            return true;
        }

        [UnityTest] public IEnumerator LongArabicAndEnglish_StayInsideTheirRows_InTheVrPanel()
        {
            var ui = Object.FindAnyObjectByType<MedSimUI>();
            T F<T>(string n) => (T)typeof(MedSimUI).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui);
            var sub = F<TMPro.TMP_Text>("subtitleText"); var stu = F<TMPro.TMP_Text>("voiceText");
            Assert.AreEqual(RenderMode.WorldSpace, sub.canvas.renderMode, "VR layout");
            var show = typeof(MedSimUI).GetMethod("ShowTranscript", BindingFlags.NonPublic | BindingFlags.Instance);
            var pat = Object.FindObjectsByType<CharacterSpeechController>(FindObjectsSortMode.None).First(c => c.persona == Persona.Patient);
            foreach (var (line, said) in new[] {
                ("إحساس تقيل كتير، كأنه في حِمل قاعد على صدري وعم يعصره. هون، بنص صدري. بينزل على إيدي الشمال، وحاسس فكّي مشدود كمان. بلّش من شي خمسة وأربعين دقيقة، كنت طالع الدرج وحامل أغراض من السوق.", "الوجع لسا موجود، تقيل بنص صدري، وبينزل على إيدي الشمال، وكمان فكي مشدود، من حوالي خمسة وأربعين دقيقة بلش وما راح حتى وأنا قاعد؟"),
                ("Good. Before you move on, think about what the ECG shows in the inferior leads and who needs to know about it right now. Then tell me which findings from the history support your impression, and what you would check before any treatment.", "I would like to ask about the radiation of the pain, previous episodes on exertion, his family history, his smoking and his allergies before we give anything.") })
            {
                show.Invoke(ui, new object[] { said }); pat.Speak(line); yield return null; yield return null;
                Assert.IsTrue(Inside(sub), "response page inside its row: " + line.Substring(0, 20));
                Assert.IsTrue(Inside(stu), "transcript page inside its row");
                var a = new Vector3[4]; var b = new Vector3[4]; sub.rectTransform.GetWorldCorners(a); stu.rectTransform.GetWorldCorners(b);
                Assert.Greater(a[0].y, b[1].y - 1e-4f, "response row is above the transcript row (no overlap)");
                pat.Stop();
            }
        }
    }
}
