using System.Collections;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Clinical;
using MedSimAI.Core;
using MedSimAI.Instruments;
using MedSimAI.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.Tests
{
    /// <summary>Desktop Mode: same scene/engine; first-person rig, desktop rendering, instruments via mouse API, door collision.</summary>
    public class DesktopModePlayTests
    {
        [UnitySetUp] public IEnumerator Load()
        {
            AIProviderManager.ForceOffline = true; DisplayModeManager.DisableAutoDesktop = true;
            yield return SceneManager.LoadSceneAsync("MedSim_ER");
            float t = 0; while ((SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
            DisplayModeManager.Instance.ForceDesktopNow();
            t = 0; while (!DesktopPlayerController.Active && t < 5) { t += Time.unscaledDeltaTime; yield return null; }
            yield return null;
        }

        [TearDown] public void Reset() { DisplayModeManager.DisableAutoDesktop = true; }

        [UnityTest] public IEnumerator DesktopRig_FirstPerson_NativeQuality_SameEyeHeight()
        {
            Assert.IsTrue(DesktopPlayerController.Active);
            var rig = Object.FindAnyObjectByType<XRPlayerController>();
            Assert.IsFalse(rig.GetComponentsInChildren<Transform>(true).Where(x => x.name == "Left Controller" || x.name == "Right Controller").Any(x => x.gameObject.activeInHierarchy), "XR controllers disabled in desktop");
            var cam = rig.origin.Camera;
            Assert.AreEqual(rig.AppliedLift + rig.desktopTrackedHeight, cam.transform.position.y - rig.transform.position.y, 0.02f, "eye height = simulated head + the +0.40 m lift");
            var urp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            Assert.AreEqual(1.0f, urp.renderScale, 1e-3, "native resolution (no XR render scaling)");
            Assert.IsTrue(urp.supportsHDR); Assert.GreaterOrEqual(urp.msaaSampleCount, 4);
            Assert.AreEqual(StereoTargetEyeMask.None, cam.stereoTargetEye);
            Assert.IsNotNull(Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).FirstOrDefault(c => c.name == "Desktop HUD"));
            yield return null;
        }

        [UnityTest] public IEnumerator DesktopInstruments_ApplyCuff_WrongSiteRejected_SameEngineEvents()
        {
            SessionManager.Instance.StartClinicalSession(true); yield return null;
            var s = ClinicalScenarioManager.Instance.Session;
            var cuff = Object.FindAnyObjectByType<BloodPressureController>();
            var zones = Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None);
            cuff.DesktopGrab(); Assert.IsTrue(cuff.IsHeld); Assert.IsTrue(s.Tracker.Has(ClinicalEvents.InstrumentPicked));
            cuff.DesktopRejectedPlacement(); Assert.IsTrue(s.Tracker.Has(ClinicalEvents.ZoneRejected));
            var arm = zones.First(z => z.zoneId == "ARM_UPPER_L");
            var probe = cuff.probePoint ? cuff.probePoint : cuff.transform;
            cuff.transform.position += arm.transform.position - probe.position; // aim the cuff at the upper arm
            yield return null; yield return null;
            Assert.AreEqual(arm, cuff.HoverZone, "zone detected exactly as with a VR hand");
            cuff.DesktopRelease(); Assert.IsTrue(cuff.Attached);
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(s.Tracker.Has("EXAM:EX_BP"), "no reading before the student presses START");
            Assert.IsTrue(cuff.Ready); Assert.IsTrue(cuff.StartMeasurement(), "START on the device");
            float w = 0; while (!s.Tracker.Has("EXAM:EX_BP") && w < 12) { w += Time.deltaTime; yield return null; }
            Assert.IsTrue(s.Tracker.Has("EXAM:EX_BP"), "BP measured through the same clinical engine");
        }

        [UnityTest] public IEnumerator ClosedDoor_BlocksWalking_OpenDoor_LetsPlayerThrough()
        {
            var door = Object.FindAnyObjectByType<HospitalDoor>(); Assert.NotNull(door);
            var rig = Object.FindAnyObjectByType<XRPlayerController>(); var cc = rig.GetComponent<CharacterController>();
            var leaf = door.leaf.GetComponentInChildren<Collider>(); Assert.NotNull(leaf, "door leaf has a collider");
            Vector3 c = leaf.bounds.center; Vector3 corridorSide = new Vector3(c.x, 0, c.z - 0.9f);
            cc.enabled = false; rig.transform.position = corridorSide; cc.enabled = true; yield return null;
            for (int i = 0; i < 60; i++) { cc.Move(Vector3.forward * 0.03f); yield return null; }
            Assert.Less(rig.transform.position.z, c.z, "closed door blocks the player");
            door.OpenBy(88); float w = 0; while (door.Angle < 80 && w < 5) { w += Time.deltaTime; yield return null; }
            for (int i = 0; i < 80; i++) { cc.Move(Vector3.forward * 0.03f); yield return null; }
            Assert.Greater(rig.transform.position.z, c.z + 0.3f, "open door lets the player into the room");
        }
    }
}
