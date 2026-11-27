using System.Collections;
using System.Linq;
using MedSimAI.Core;
using MedSimAI.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace MedSimAI.PlayTests
{
    /// <summary>v0.4 VR locomotion wiring (headset-free): left stick smooth move, right stick turn + teleport, settings, collisions.</summary>
    public class VRLocomotionPlayTests
    {
        [UnitySetUp] public IEnumerator Load()
        {
            foreach (var k in new[] { VRLocomotion.PrefSmooth, VRLocomotion.PrefSpeed, VRLocomotion.PrefSmoothTurn, VRLocomotion.PrefSnap, VRLocomotion.PrefDead, VRLocomotion.PrefTurnSpeed }) PlayerPrefs.DeleteKey(k);
            MedSimAI.AI.AIProviderManager.ForceOffline = true; DisplayModeManager.DisableAutoDesktop = true;
            yield return SceneManager.LoadSceneAsync("MedSim_ER");
            float t = 0; while ((SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
            yield return null;
        }

        [UnityTest] public IEnumerator LeftStickMoves_RightStickTurnsAndTeleports_SettingsPersist()
        {
            var rig = Object.FindAnyObjectByType<XRPlayerController>();
            var loco = rig.GetComponent<VRLocomotion>(); Assert.IsNotNull(loco, "VR locomotion configured on the rig");
            var mgrs = rig.GetComponentsInChildren<ControllerInputActionManager>(true);
            var left = mgrs.First(m => m.name.Contains("Left")); var right = mgrs.First(m => m.name.Contains("Right"));
            Assert.IsTrue(left.smoothMotionEnabled, "left thumbstick = smooth movement");
            Assert.IsFalse(right.smoothMotionEnabled, "right thumbstick keeps teleport (forward)");
            Assert.IsFalse(right.smoothTurnEnabled, "snap turn by default");
            Assert.IsTrue(rig.smoothMoveProvider.activeSelf, "continuous move provider active");
            var move = rig.GetComponentInChildren<DynamicMoveProvider>(true);
            Assert.AreEqual(1.2f, move.moveSpeed, 1e-3f); Assert.IsTrue(move.enableStrafe); Assert.IsFalse(move.enableFly);
            Assert.AreEqual(DynamicMoveProvider.MovementDirection.HeadRelative, move.leftHandMovementDirection, "moves where you look");
            Assert.AreEqual(0.2f, InputSystem.settings.defaultDeadzoneMin, 1e-3f);
            Assert.GreaterOrEqual(rig.GetComponent<CharacterController>().radius, 0.2f, "body radius keeps the player out of walls and equipment");
            loco.CycleSpeed(); loco.ToggleSmoothTurn(); loco.CycleDeadZone(); yield return null;
            Assert.AreEqual(1.6f, move.moveSpeed, 1e-3f); Assert.IsTrue(right.smoothTurnEnabled); Assert.AreEqual(0.3f, InputSystem.settings.defaultDeadzoneMin, 1e-3f);
            Assert.AreEqual(1.6f, PlayerPrefs.GetFloat(VRLocomotion.PrefSpeed), 1e-3f, "saved between sessions");
            loco.ToggleSmoothMove(); yield return null;
            Assert.IsFalse(left.smoothMotionEnabled); Assert.IsFalse(rig.smoothMoveProvider.activeSelf, "teleport-only comfort option");
        }

        [UnityTest] public IEnumerator DesktopHeight_Unchanged_ByVrHeightSetting()
        {
            var rig = Object.FindAnyObjectByType<XRPlayerController>();
            rig.SetExtraHeight(0.8f);
            DisplayModeManager.Instance.ForceDesktopNow();
            float t = 0; while (!DesktopPlayerController.Active && t < 5) { t += Time.unscaledDeltaTime; yield return null; }
            yield return null;
            Assert.AreEqual(XRPlayerController.DesktopLift, rig.AppliedLift, 1e-4f, "Desktop Mode keeps +0.40 m");
            var cam = rig.origin.Camera;
            Assert.AreEqual(0.40f + rig.desktopTrackedHeight, cam.transform.position.y - rig.transform.position.y, 0.02f);
            PlayerPrefs.DeleteKey(XRPlayerController.PrefExtraHeight);
        }
    }
}
