using System.Collections;
using MedSimAI.Core;
using MedSimAI.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MedSimAI.PlayTests
{
    public class PlayerHeightPlayTests
    {
        XRPlayerController rig;

        [UnitySetUp] public IEnumerator Load()
        {
            PlayerPrefs.DeleteKey(XRPlayerController.PrefExtraHeight); PlayerPrefs.DeleteKey(XRPlayerController.PrefSeated);
            MedSimAI.AI.AIProviderManager.ForceOffline = true; // automated tests never make billable AI calls
            yield return SceneManager.LoadSceneAsync("MedSim_ER");
            float t = 0; while ((SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
            rig = Object.FindAnyObjectByType<XRPlayerController>();
            yield return null;
        }

        float Offset => rig.origin.CameraFloorOffsetObject.transform.localPosition.y;

        [UnityTest] public IEnumerator DefaultLift_Is40cm_OnCameraOffset_RootStaysOnFloor()
        {
            yield return null;
            Assert.AreEqual(0.40f, rig.extraHeight, 1e-4f);
            Assert.AreEqual(0.40f, Offset, 1e-3f, "Camera Offset raised by 0.40 m (Floor mode base = 0)");
            Assert.Less(Mathf.Abs(rig.origin.transform.position.y), 0.05f, "rig root stays on the floor (within the starter rig's character-controller skin)");
        }

        [UnityTest] public IEnumerator ControllersAndHead_ShareTheLift()
        {
            yield return null;
            var offset = rig.origin.CameraFloorOffsetObject.transform;
            Assert.IsTrue(rig.origin.Camera.transform.IsChildOf(offset), "headset camera under Camera Offset");
            int controllers = 0;
            foreach (Transform t in offset.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("Controller") && !t.name.Contains("Stabilized")) controllers++;
            Assert.GreaterOrEqual(controllers, 2, "both controllers under Camera Offset → raised together");
        }

        [UnityTest] public IEnumerator Height_Adjusts_Clamps_AndPersists()
        {
            rig.SetExtraHeight(0.75f); yield return null;
            Assert.AreEqual(0.75f, Offset, 1e-3f);
            Assert.AreEqual(0.75f, PlayerPrefs.GetFloat(XRPlayerController.PrefExtraHeight), 1e-4f);
            rig.SetExtraHeight(2f); yield return null; Assert.AreEqual(1f, rig.extraHeight, 1e-4f);
            rig.SetExtraHeight(-1f); yield return null; Assert.AreEqual(0f, rig.extraHeight, 1e-4f);
            rig.SetSeated(true); rig.SetExtraHeight(0.4f); yield return null;
            Assert.AreEqual(0.4f + rig.seatedLift, Offset, 1e-3f);
            rig.ResetHeight(); yield return null; Assert.AreEqual(0.40f, Offset, 1e-3f); Assert.IsFalse(rig.Seated);
        }

        [UnityTest] public IEnumerator Lift_SurvivesTeleport_Recenter_AndOriginReset()
        {
            rig.SetExtraHeight(0.5f); yield return null;
            rig.origin.transform.position += new Vector3(-0.8f, 0f, 0.6f); // teleport moves the rig root
            yield return null; Assert.AreEqual(0.5f, Offset, 1e-3f);
            rig.Recenter(); yield return null; Assert.AreEqual(0.5f, Offset, 1e-3f);
            var t = rig.origin.CameraFloorOffsetObject.transform; t.localPosition = new Vector3(0, 0, 0); // XR Origin resetting its offset
            yield return null; Assert.AreEqual(0.5f, Offset, 1e-3f, "re-applied every LateUpdate");
            rig.ResetHeight();
        }

        [UnityTest] public IEnumerator Environment_IsNeverRescaled()
        {
            var bed = GameObject.Find("Hospital Bed"); var patient = GameObject.Find("Patient - Mr Adam Reyes");
            rig.SetExtraHeight(1f); yield return null;
            Assert.AreEqual(Vector3.one, bed.transform.lossyScale); Assert.AreEqual(Vector3.one, patient.transform.lossyScale);
            rig.ResetHeight();
        }

        [UnityTest] public IEnumerator GraphicsProfiles_SwitchAtRuntime_WithoutTouchingAssets()
        {
            var pm = PerformanceManager.Instance;
            pm.Apply(GraphicsProfile.HighQuality, persist: false); yield return null;
            var a = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            Assert.AreEqual(1.1f, a.renderScale, 1e-4f); Assert.AreEqual(4, a.msaaSampleCount);
            Assert.IsTrue(a.name.Contains("runtime"), "runtime clone, project asset untouched");
            pm.Apply(GraphicsProfile.Performance, persist: false); yield return null;
            a = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            Assert.AreEqual(0.9f, a.renderScale, 1e-4f);
            var exam = GameObject.Find("Exam Spot").GetComponent<Light>();
            Assert.AreEqual(LightShadows.Hard, exam.shadows);
            pm.Apply(GraphicsProfile.Balanced, persist: false); yield return null;
            Assert.AreEqual(LightShadows.Soft, exam.shadows);
        }

        [UnityTest] public IEnumerator ExaminationTasks_RemainReachable_AtMaxLift()
        {
            rig.SetExtraHeight(1f); yield return null;
            // Interaction uses physical hand position, which is lifted too: with ray interaction (far grab) and
            // teleport, instruments on the trolley stay inside the default ray length from the lifted head.
            var trolleyTop = GameObject.Find("TopShelf").transform.position;
            var head = rig.origin.CameraFloorOffsetObject.transform.position + Vector3.up * 1.25f;
            Assert.Less(Vector3.Distance(head, trolleyTop), 10f, "within XRI far-interaction range");
            rig.ResetHeight();
        }
    }
}
