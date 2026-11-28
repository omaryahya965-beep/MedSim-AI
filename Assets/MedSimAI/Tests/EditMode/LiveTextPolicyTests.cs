using MedSimAI.UI;
using NUnit.Framework;

namespace MedSimAI.Tests
{
    /// <summary>The live experience shows no draft/validation ribbons (owner's request); other text is untouched.</summary>
    public class LiveTextPolicyTests
    {
        [Test]
        public void DraftWording_IsRemoved_OtherTextKept()
        {
            Assert.AreEqual("Educational simulation", LiveTextPolicy.Clean("Educational simulation · DRAFT — not clinically validated"));
            Assert.AreEqual("SIMULATION", LiveTextPolicy.Clean("DRAFT"));
            Assert.AreEqual("<b>BAY 3</b>\nMonitoring: 3-lead ECG attached", LiveTextPolicy.Clean("<b>BAY 3</b>\nMonitoring: 3-lead ECG attached\n<color=#B71C1C>DRAFT — NOT CLINICALLY VALIDATED</color>"));
            Assert.AreEqual("Room 01 · Clinical Examination", LiveTextPolicy.Clean("Room 01 · Clinical Examination"));
        }
    }
}
