using System.Collections;
using MedSimAI.AI;
using MedSimAI.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MedSimAI.Tests
{
    /// <summary>VR start-up recovery: a launch whose OpenXR start-up never completed makes the next launch start in Desktop Mode with a notice.</summary>
    public class XrRecoveryPlayTests
    {
        [UnityTest] public IEnumerator PendingXrStartup_FromPreviousLaunch_StartsDesktop_WithNotice_AndClearsFlag()
        {
            AIProviderManager.ForceOffline = true; DisplayModeManager.DisableAutoDesktop = true;
            var prevForced = DisplayModeManager.ForcedPreference;
            DisplayModeManager.ForcedPreference = DisplayModePreference.Auto;
            PlayerPrefs.SetInt(DisplayModeManager.XrPendingKey, 1);
            try
            {
                if (DisplayModeManager.Instance) Object.DestroyImmediate(DisplayModeManager.Instance.gameObject);
                var go = new GameObject("[test display mode]"); var dm = go.AddComponent<DisplayModeManager>();
                float t = 0; while (DisplayModeManager.Mode == DisplayMode.Detecting && t < 5) { t += Time.unscaledDeltaTime; yield return null; }
                Assert.AreEqual(DisplayMode.Desktop, DisplayModeManager.Mode, dm.Detail);
                StringAssert.Contains("recovered", dm.Detail);
                Assert.IsNotEmpty(DisplayModeManager.RecoveryNotice);
                Assert.AreEqual(0, PlayerPrefs.GetInt(DisplayModeManager.XrPendingKey, 0), "flag cleared so the next launch tries VR again");
                Object.Destroy(go);
            }
            finally { DisplayModeManager.ForcedPreference = prevForced; PlayerPrefs.DeleteKey(DisplayModeManager.XrPendingKey); }
        }
    }
}
