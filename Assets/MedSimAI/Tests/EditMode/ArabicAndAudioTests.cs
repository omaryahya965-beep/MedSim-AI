using System.Linq;
using System.Reflection;
using MedSimAI.AI;
using MedSimAI.Clinical;
using MedSimAI.UI;
using MedSimAI.Voice;
using NUnit.Framework;

namespace MedSimAI.Tests
{
    /// <summary>v0.4: language switching/detection, RTL rendering of mixed Arabic transcripts, complete streamed playback.</summary>
    public class ArabicAndAudioTests
    {
        [TestCase("احكي عربي", "ar")] [TestCase("حول عربي", "ar")] [TestCase("خلينا نحكي بالعربي", "ar")] [TestCase("بدي تحكي معي عربي", "ar")]
        [TestCase("كمل بالعربي", "ar")] [TestCase("غير اللغة للعربي", "ar")] [TestCase("Speak Arabic", "ar")] [TestCase("ممكن تحكي معي بالعربي لو سمحت؟", "ar")]
        [TestCase("Speak English", "en")] [TestCase("switch to English please", "en")] [TestCase("احكي انجليزي", "en")] [TestCase("رجعنا للانكليزي", "en")]
        public void LanguageCommands_AreRecognised(string said, string lang) => Assert.AreEqual(lang, LanguageCommands.DetectCommand(said));

        [TestCase("من إيمتى بلش الوجع؟")] [TestCase("عندك chest pain من زمان؟")] [TestCase("Are you allergic to any medications?")]
        [TestCase("How do you say chest pain in Arabic and what does it mean for this patient here?")]
        public void ClinicalQuestions_AreNotLanguageCommands(string said) => Assert.IsNull(LanguageCommands.DetectCommand(said));

        [Test] public void LanguageDetection_ArabicMixedWithEnglishMedicalTerms_IsArabic()
        {
            Assert.AreEqual("ar", LanguageCommands.DetectLanguage("عندك chest pain من زمان؟"));
            Assert.AreEqual("ar", LanguageCommands.DetectLanguage("وين الوجع بالزبط؟"));
            Assert.AreEqual("en", LanguageCommands.DetectLanguage("Where exactly is the pain?"));
        }

        [Test] public void Rtl_PureArabic_IsShapedOnly_AndEnglishRunsSurviveTmpReversal()
        {
            // pure Arabic: same as the subtitle path always did (shaping, no reordering by us)
            var pure = "من إيمتى بلش الوجع؟";
            Assert.AreEqual(ArabicShaper.Shape(pure), ArabicShaper.PrepareRtl(pure));
            // mixed: TMP's RTL mode reverses the whole sequence; after that reversal English and numbers must read normally
            var mixed = "أنت: عندك chest pain من 45 دقيقة؟";
            var prepared = ArabicShaper.PrepareRtl(mixed);
            var asDisplayed = new string(prepared.Reverse().ToArray()); // what TMP lays out left-to-right
            StringAssert.Contains("chest pain", asDisplayed);
            StringAssert.Contains("45", asDisplayed);
            Assert.IsFalse(asDisplayed.Contains("niap") || asDisplayed.Contains("54"), "no double reversal");
        }

        [Test] public void StreamingVoice_IsNotFinished_UntilThePlayheadPassesTheLastSample()
        {
            var v = new StreamingVoice();
            var read = typeof(StreamingVoice).GetMethod("OnRead", BindingFlags.NonPublic | BindingFlags.Instance);
            void Read(int n) => read.Invoke(v, new object[] { new float[n] });
            Read(1000);                               // Unity buffers ahead before any data arrived → 1000 samples of silence
            v.PushPcm16(new byte[4800], 0, 4800);     // 2400 samples of speech arrive
            v.Complete();
            Read(4096);                               // Unity reads ahead: the whole line is now "consumed"
            Assert.IsTrue(v.Drained, "callback has consumed everything");
            int start = v.StartClipPosition;
            Assert.Greater(start, 1000, "Unity pre-read silence (at creation and while waiting) is skipped: playback starts at the first real sample");
            Assert.AreEqual(start + 2400, v.LastDataClipPosition);
            Assert.IsFalse(v.PlayedOut(start + 1000), "playhead still inside the line → keep playing (this used to be cut off)");
            Assert.IsTrue(v.PlayedOut(start + 2400));
        }

        [Test] public void ArabicPack_TranslatesInterventionsAndDiscussionQuestions()
        {
            var s = ScenarioData.LoadFromResources("ER_CHEST_PAIN_001");
            var pack = LanguagePack.Load(s, "ar");
            var iv = s.interventions.First(i => i.id == "I_ALLERGY_CHECK").lines[0];
            Assert.IsTrue(ArabicShaper.ContainsArabic(pack.Localize(iv)), "intervention has approved Arabic wording");
            var q = s.discussion.First().question;
            Assert.IsTrue(ArabicShaper.ContainsArabic(pack.Localize(q)), "discussion question has approved Arabic wording");
            Assert.IsTrue(ArabicShaper.ContainsArabic(pack.Localize(s.instructorLines.discussionIntro + " " + q)), "composite line localised");
        }
    }
}
