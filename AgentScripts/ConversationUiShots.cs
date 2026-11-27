// Conversation panel layout check in Play mode (text-only lines, no API calls). Captures the real screen and verifies
// programmatically that every text's rendered page lies inside its own row and that rows never overlap.
//   unity command script eval -- --file AgentScripts/ConversationUiShots.cs --timeout 180
using UnityEngine; using System; using System.IO; using System.Linq; using System.Reflection; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.UI; using MedSimAI.Characters; using MedSimAI.AI; using MedSimAI.Core;
string dir = "D:/Medical-Test/My project/Screenshots/ConversationUI/"; Directory.CreateDirectory(dir);
var ui = UnityEngine.Object.FindAnyObjectByType<MedSimUI>();
T F<T>(string n) => (T)typeof(MedSimUI).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui);
var sub = F<TMPro.TMP_Text>("subtitleText"); var stu = F<TMPro.TMP_Text>("voiceText"); var name = F<TMPro.TMP_Text>("speakerText");
var show = typeof(MedSimUI).GetMethod("ShowTranscript", BindingFlags.NonPublic | BindingFlags.Instance);
var sp = UnityEngine.Object.FindObjectsByType<CharacterSpeechController>(FindObjectsSortMode.None); var pat = sp.First(c => c.persona == Persona.Patient); var doc = sp.First(c => c.persona == Persona.Instructor);
var V = MedSimAI.Voice.VoiceInteractionManager.Instance; V.Interrupt();
var log = new List<string>();
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
Rect WorldRect(RectTransform r) { var c = new Vector3[4]; r.GetWorldCorners(c); return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y); }
bool Inside(TMPro.TMP_Text t)
{
  t.ForceMeshUpdate(); var ti = t.textInfo; var box = WorldRect(t.rectTransform); bool ok = true; int page = t.pageToDisplay - 1;
  for (int i = 0; i < ti.characterCount; i++) { var ch = ti.characterInfo[i]; if (!ch.isVisible || ch.pageNumber != page) continue;
    var bl = t.transform.TransformPoint(ch.bottomLeft); var tr = t.transform.TransformPoint(ch.topRight);
    if (bl.x < box.xMin - 1 || tr.x > box.xMax + 1 || bl.y < box.yMin - 2 || tr.y > box.yMax + 2) { ok = false; break; } }
  return ok;
}
async Task Case(string id, CharacterSpeechController who, string line, string transcript, float waitProgress = 0.15f)
{
  show.Invoke(ui, new object[] { transcript });
  who.Speak(line); // text-only (no clip): duration from text length; raises the same subtitle events as live speech
  float t0 = Time.realtimeSinceStartup; while (who.Progress < waitProgress && Time.realtimeSinceStartup - t0 < 20) await Task.Yield();
  await W(0.1f); await Awaitable.EndOfFrameAsync();
  var tex = ScreenCapture.CaptureScreenshotAsTexture(2); File.WriteAllBytes(dir + id + ".png", tex.EncodeToPNG()); UnityEngine.Object.Destroy(tex);
  bool overlap = WorldRect(sub.rectTransform).Overlaps(WorldRect(stu.rectTransform));
  log.Add($"{id}: name='{name.GetParsedText()}' response pages={sub.textInfo.pageCount} showing {sub.pageToDisplay} inside={Inside(sub)} rtl={sub.isRightToLeftText} align={sub.horizontalAlignment} | transcript pages={stu.textInfo.pageCount} inside={Inside(stu)} rtl={stu.isRightToLeftText} | rows overlap={overlap}");
  who.Stop();
}
var longAr = "إحساس تقيل كتير، كأنه في حِمل قاعد على صدري وعم يعصره. هون، بنص صدري. بينزل على إيدي الشمال، وحاسس فكّي مشدود كمان. بلّش من شي خمسة وأربعين دقيقة، كنت طالع الدرج وحامل أغراض من السوق، وما راح أبداً حتى وأنا قاعد مرتاح. عم بعرق كتير وحاسس حالي بدي أرجّع، ونفَسي ضيق شوي كمان.";
var longStu = "الوجع لسا موجود، تقيل بنص صدري، كأنه وزن واقف على قلبي وبينزل على إيدي الشمال، وكمان فكي مشدود، من حوالي خمسة وأربعين دقيقة بلش وما راح، حتى وأنا قاعد، وكنت أحس بشوية ضيق لما أطلع طلوع بس كانت تروح بسرعة؟";
await Case("01_short_arabic", pat, "عندي حساسية من البنسلين، طلعلي منه طفح.", "عندك أي حساسية لأي دوا؟");
await Case("02_long_arabic_page1", pat, longAr, longStu, 0.05f);
await Case("03_long_arabic_page2_midspeech", pat, longAr, longStu, 0.55f);
await Case("04_mixed_ar_en_numbers", doc, "تمام، رح أطلب ECG هلّق؛ ضغطه 152/94 والنبض 98، والتشبّع 96%. شو بتتوقع يطلع بالـ troponin؟", "دكتورة، بدي أعمل 12-lead ECG وبدي أعرف الـ SpO2 كمان (هلّق)؟");
await Case("05_long_english", doc, "Good. Before you move on, think about what the ECG shows in the inferior leads and who needs to know about it right now. Then tell me which findings from the history support your impression, and what you would want to check before any treatment is given to Mr. Reyes, including allergies and his regular medications.", "I would like to ask about the radiation of the pain, any previous episodes on exertion, his family history of heart disease, his smoking, and whether he has any allergies before we give anything.", 0.1f);
await Case("06_english_short", pat, "About eight out of ten.", "How bad is the pain from zero to ten?");
File.WriteAllLines(dir + "layout_checks.txt", log);
return string.Join("\n", log);
