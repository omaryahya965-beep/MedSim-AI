using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace MedSimAI.XR
{
    /// <summary>
    /// VR thumbstick locomotion on the XRI starter rig (VIVE Focus 3 via OpenXR: thumbsticks map to primary2DAxis).
    ///  • Left stick: smooth continuous movement relative to where you LOOK (forward/back/strafe/diagonal).
    ///  • Right stick: turn (snap by default, or smooth); push forward to teleport (kept).
    ///  • Grip = grab, trigger = UI/activate, A/X = push-to-talk, menu = pause — no stick conflicts.
    /// Movement goes through the rig's CharacterController, so walls, furniture, equipment, characters and the closed door
    /// block it. Settings are saved on this device. Desktop Mode uses its own keyboard/mouse controller (unchanged).
    /// </summary>
    public class VRLocomotion : MonoBehaviour
    {
        public const string PrefSmooth = "medsim.vr.smoothMove", PrefSpeed = "medsim.vr.moveSpeed", PrefSmoothTurn = "medsim.vr.smoothTurn",
            PrefSnap = "medsim.vr.snapAngle", PrefTurnSpeed = "medsim.vr.turnSpeed", PrefDead = "medsim.vr.deadzone";
        public static readonly float[] Speeds = { 0.8f, 1.2f, 1.6f, 2.0f }, DeadZones = { 0.1f, 0.2f, 0.3f }, SnapAngles = { 30f, 45f, 60f }, TurnSpeeds = { 60f, 90f, 120f };

        public bool SmoothMove { get; private set; } = true;
        public float MoveSpeed { get; private set; } = 1.2f;
        public bool SmoothTurn { get; private set; }
        public float SnapAngle { get; private set; } = 45f;
        public float TurnSpeed { get; private set; } = 90f;
        public float DeadZone { get; private set; } = 0.2f;
        public event Action Changed;

        XRPlayerController player; ControllerInputActionManager left, right; DynamicMoveProvider move; SnapTurnProvider snap; ContinuousTurnProvider cturn; CharacterController cc;

        void Awake()
        {
            SmoothMove = PlayerPrefs.GetInt(PrefSmooth, 1) == 1;
            MoveSpeed = PlayerPrefs.GetFloat(PrefSpeed, 1.2f);
            SmoothTurn = PlayerPrefs.GetInt(PrefSmoothTurn, 0) == 1;
            SnapAngle = PlayerPrefs.GetFloat(PrefSnap, 45f);
            TurnSpeed = PlayerPrefs.GetFloat(PrefTurnSpeed, 90f);
            DeadZone = PlayerPrefs.GetFloat(PrefDead, 0.2f);
        }

        void Start() { Find(); Apply(); }

        void Find()
        {
            player = GetComponent<XRPlayerController>();
            foreach (var m in GetComponentsInChildren<ControllerInputActionManager>(true))
            {
                if (m.gameObject.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0) left = m;
                else if (m.gameObject.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0) right = m;
            }
            move = GetComponentInChildren<DynamicMoveProvider>(true);
            snap = GetComponentInChildren<SnapTurnProvider>(true);
            cturn = GetComponentInChildren<ContinuousTurnProvider>(true);
            cc = GetComponent<CharacterController>();
        }

        /// <summary>Applies the settings to the rig (VR only; Desktop Mode has its own controller).</summary>
        public void Apply()
        {
            if (DesktopPlayerController.Active) return;
            if (player && player.smoothMoveProvider) player.smoothMoveProvider.SetActive(SmoothMove);
            if (left) { left.smoothMotionEnabled = SmoothMove; left.smoothTurnEnabled = SmoothTurn; }
            if (right) { right.smoothMotionEnabled = false; right.smoothTurnEnabled = SmoothTurn; } // right stick: turn + teleport
            if (move)
            {
                move.moveSpeed = MoveSpeed; move.enableStrafe = true; move.enableFly = false;
                move.leftHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
                move.rightHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
            }
            if (snap) { snap.turnAmount = SnapAngle; snap.enableTurnLeftRight = true; snap.enableTurnAround = false; }
            if (cturn) cturn.turnSpeed = TurnSpeed;
            InputSystem.settings.defaultDeadzoneMin = DeadZone; // the starter actions use StickDeadzone with default parameters
            if (cc) { cc.radius = 0.2f; cc.stepOffset = Mathf.Min(cc.stepOffset, 0.25f); cc.slopeLimit = 45f; }
            PlayerPrefs.SetInt(PrefSmooth, SmoothMove ? 1 : 0); PlayerPrefs.SetFloat(PrefSpeed, MoveSpeed); PlayerPrefs.SetInt(PrefSmoothTurn, SmoothTurn ? 1 : 0);
            PlayerPrefs.SetFloat(PrefSnap, SnapAngle); PlayerPrefs.SetFloat(PrefTurnSpeed, TurnSpeed); PlayerPrefs.SetFloat(PrefDead, DeadZone); PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static float Next(float[] a, float v) { int i = Array.FindIndex(a, x => Mathf.Approximately(x, v)); return a[(i + 1) % a.Length]; }
        public void ToggleSmoothMove() { SmoothMove = !SmoothMove; Apply(); }
        public void CycleSpeed() { MoveSpeed = Next(Speeds, MoveSpeed); Apply(); }
        public void ToggleSmoothTurn() { SmoothTurn = !SmoothTurn; Apply(); }
        public void CycleSnapAngle() { SnapAngle = Next(SnapAngles, SnapAngle); Apply(); }
        public void CycleTurnSpeed() { TurnSpeed = Next(TurnSpeeds, TurnSpeed); Apply(); }
        public void CycleDeadZone() { DeadZone = Next(DeadZones, DeadZone); Apply(); }

        public string MoveLabel => SmoothMove ? $"Move: left stick, {MoveSpeed:0.0} m/s" : "Move: teleport only";
        public string TurnLabel => SmoothTurn ? $"Turn: smooth {TurnSpeed:0}°/s (right stick)" : $"Turn: snap {SnapAngle:0}° (right stick)";
        public string DeadZoneLabel => $"Stick dead zone: {DeadZone:0.00}";
    }
}
