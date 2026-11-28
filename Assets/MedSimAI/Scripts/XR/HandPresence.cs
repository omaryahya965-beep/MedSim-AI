using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MedSimAI.XR
{
    /// <summary>
    /// Visible hands for the student (no external assets; procedural nitrile gloves, see <see cref="GloveHand"/>).
    ///   • VR: a glove on each controller replaces the generic controller model; the fingers follow the grip trigger and
    ///     close fully while the hand holds something, so an instrument reads as held in the hand.
    ///   • Desktop: a right glove appears around the instrument being held (clinical or surgical), gripping its handle.
    /// Visual only: interaction, attach points and validation are unchanged.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class HandPresence : MonoBehaviour
    {
        public static HandPresence Instance { get; private set; }
        /// <summary>Other systems that hold items in Desktop Mode (e.g. the operating room) report the held transform here.</summary>
        public static Func<Transform> ExtraHeld;
        GloveHand vrLeft, vrRight, deskRight; Transform leftCtl, rightCtl; GameObject leftModel, rightModel;
        IXRSelectInteractor leftSel, rightSel; DesktopPlayerController desktop;
        public bool DesktopGloveVisible => deskRight && deskRight.gameObject.activeSelf;

        public static HandPresence Install()
        {
            if (Instance) return Instance;
            var rig = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>(); if (!rig) return null;
            return rig.gameObject.AddComponent<HandPresence>();
        }

        void Awake()
        {
            Instance = this;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Left Controller") leftCtl = t; else if (t.name == "Right Controller") rightCtl = t;
                else if (t.name == "Left Controller Visual") leftModel = t.gameObject; else if (t.name == "Right Controller Visual") rightModel = t.gameObject;
            }
            if (leftCtl) { vrLeft = GloveHand.Create(leftCtl, false, "VR Glove L"); Pose(vrLeft.transform, false); leftSel = leftCtl.GetComponentInChildren<NearFarInteractor>(true); }
            if (rightCtl) { vrRight = GloveHand.Create(rightCtl, true, "VR Glove R"); Pose(vrRight.transform, true); rightSel = rightCtl.GetComponentInChildren<NearFarInteractor>(true); }
            var cam = Camera.main;
            deskRight = GloveHand.Create(cam ? cam.transform : transform, true, "Desktop Glove R"); deskRight.gameObject.SetActive(false);
            if (!TryGetComponent(out desktop)) desktop = FindAnyObjectByType<DesktopPlayerController>();
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Glove relative to the controller: palm around the handle, fingers forward, palm facing the body midline.</summary>
        static void Pose(Transform glove, bool right)
        {
            glove.localPosition = new Vector3(right ? 0.012f : -0.012f, -0.022f, -0.055f);
            glove.localRotation = Quaternion.Euler(-12f, 0f, right ? -90f : 90f);
        }

        void LateUpdate()
        {
            bool vr = DisplayModeManager.Mode == DisplayMode.VR;
            Vr(vr); Desk(true);   // desktop holding paths are only active in Desktop Mode, so the desktop glove follows them directly
        }

        void Vr(bool on)
        {
            foreach (var (g, model, node, sel) in new[] { (vrLeft, leftModel, XRNode.LeftHand, leftSel), (vrRight, rightModel, XRNode.RightHand, rightSel) })
            {
                if (!g) continue;
                if (g.gameObject.activeSelf != on) g.gameObject.SetActive(on);
                if (model && model.activeSelf == on) model.SetActive(!on);   // the glove replaces the generic controller model
                if (!on) continue;
                float grip = 0f; var dev = InputDevices.GetDeviceAtXRNode(node);
                if (dev.isValid && dev.TryGetFeatureValue(CommonUsages.grip, out var gv)) grip = gv;
                g.targetCurl = sel != null && sel.hasSelection ? 0.9f : Mathf.Lerp(0.15f, 0.85f, grip);
            }
        }

        void Desk(bool on)
        {
            Transform held = null;
            if (on)
            {
                if (desktop && desktop.Held) held = desktop.Held.transform;
                if (!held && ExtraHeld != null) held = ExtraHeld();
            }
            bool show = held != null;
            if (deskRight.gameObject.activeSelf != show) deskRight.gameObject.SetActive(show);
            if (!show) return;
            var cam = Camera.main; if (!cam) return;
            if (deskRight.transform.parent != cam.transform) deskRight.transform.SetParent(cam.transform, false);
            var grip = held.Find("GripPoint") ?? held.Find("Attach") ?? held;
            // power grip around the handle: the hand sits below-right of the grip (as seen by the student), palm toward the tool,
            // fingers wrapping around the tool's long axis
            var axis = grip.forward; if (axis.sqrMagnitude < 1e-6f) axis = cam.transform.forward;
            var outward = Vector3.ProjectOnPlane(cam.transform.right * 0.55f - cam.transform.up * 0.85f, axis);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.ProjectOnPlane(-cam.transform.up, axis);
            outward.Normalize();
            var fingers = Vector3.Cross(axis, outward).normalized;   // perpendicular to the tool and to the palm normal
            var t = deskRight.transform;
            t.SetPositionAndRotation(grip.position + outward * 0.032f - axis * 0.01f, Quaternion.LookRotation(fingers, outward));
            deskRight.targetCurl = 0.85f;
        }
    }
}
