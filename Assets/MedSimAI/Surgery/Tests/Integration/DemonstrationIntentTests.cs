using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Shared;
using NUnit.Framework;

namespace MedSimAI.Surgery.Tests
{
    /// <summary>"Show me / do it in front of me / I want to learn from you" — in English, Palestinian Arabic and MSA — is
    /// recognised as a request for a practical demonstration (both rooms), and common look-alikes are not.</summary>
    public class DemonstrationIntentTests
    {
        [TestCase("I don't have enough information.")]
        [TestCase("Show me how to do it.")]
        [TestCase("Please demonstrate.")]
        [TestCase("خليني أشوف كيف بتعملي الفحص")]
        [TestCase("ورجيني كيف بنستخدم جهاز الضغط")]
        [TestCase("Show me how to use the pen.")]
        [TestCase("ورجيني كيف أعلم على الركبة.")]
        [TestCase("اعملها قدامي.")]
        [TestCase("Can you do it first? I want to watch you.")]
        [TestCase("أنا خبرة العملية كتير ضعيفة، فأنا حسا بدي إياك تروح تشوفي المريض وتحكي معاه وتشوفي كيف تحكي معاه وتطبق عملياً، وأنا أتعلّم منك.")]
        public void DemonstrationRequests_AreRecognised(string u) => Assert.IsTrue(DemonstrationIntent.Detect(u).demo, u);

        [TestCase("خديني على غرفة العمليات")]
        [TestCase("Take me to the operating room")]
        [TestCase("Where is the scalpel?")]
        [TestCase("Can you show me where it hurts?")]
        [TestCase("Pass me the scalpel")]
        [TestCase("What is his blood pressure?")]
        public void LookAlikes_AreNotDemonstrations(string u) => Assert.IsFalse(DemonstrationIntent.Detect(u).demo, u);

        [Test]
        public void Topic_Full_AndStop()
        {
            Assert.AreEqual("bp_cuff", DemonstrationIntent.Detect("ورجيني كيف بنستخدم جهاز الضغط").topic);
            Assert.AreEqual("stethoscope", DemonstrationIntent.Detect("show me how to listen with the stethoscope").topic);
            Assert.AreEqual("pulse_oximeter", DemonstrationIntent.Detect("demonstrate the oxygen clip on the finger").topic);
            Assert.AreEqual("interview", DemonstrationIntent.Detect("وريني كيف تحكي معه وتسأله").topic);
            Assert.IsTrue(DemonstrationIntent.Detect("Demonstrate the procedure from start to finish").full);
            Assert.IsTrue(DemonstrationIntent.Detect("اعمليها قدامي، العملية كاملة").full);
            Assert.IsFalse(DemonstrationIntent.Detect("Show me the full step.").full, "the full STEP is the current step");
            Assert.IsTrue(DemonstrationIntent.Detect("خلص، خليني أجرب").stop);
            Assert.IsTrue(DemonstrationIntent.Detect("OK that's enough, let me try").stop);
        }

        [Test]
        public void SurgicalParser_MapsToDemonstrationActions()
        {
            Assert.AreEqual(SurgeryActionCatalog.DemonstrateStep, SurgeryIntentParser.Parse("Sami, show me how to use the pen").action.action);
            Assert.AreEqual(SurgeryCharacterIds.Assistant, SurgeryIntentParser.Parse("Sami, show me how to use the pen").action.characterId, "the addressed character demonstrates");
            Assert.AreEqual(SurgeryActionCatalog.DemonstrateProcedure, SurgeryIntentParser.Parse("دكتورة، اعمليها قدامي من البداية للنهاية").action.action);
            Assert.AreEqual(SurgeryActionCatalog.HandInstrument, SurgeryIntentParser.Parse("Sami, pass me the scalpel").action.action, "hand-over still a hand-over");
            var v = SurgeryActionValidator.Validate(new ProposedAction { characterId = SurgeryCharacterIds.Surgeon, action = SurgeryActionCatalog.DemonstrateStep }, new ActionContext());
            Assert.AreEqual(ActionStatus.Accepted, v.status); Assert.AreEqual("", v.lineKey, "a demonstration narrates itself");
        }
    }
}
