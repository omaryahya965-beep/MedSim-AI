using UnityEngine;

namespace MedSimAI.Surgery.Instruments
{
    /// <summary>
    /// Documents one movable sub-part of an instrument: the pivot is this transform, motion is about/along <see cref="localAxis"/>.
    /// <see cref="SetNormalized"/> is a neutral helper (poses the part between min and max) — not an interaction mechanic.
    /// </summary>
    [DisallowMultipleComponent]
    public class InstrumentMovablePart : MonoBehaviour
    {
        public enum Motion { Rotate, Spin, Translate }
        public string partId = "";
        public Motion motion = Motion.Rotate;
        public Vector3 localAxis = Vector3.up;
        [Tooltip("Degrees for Rotate/Spin (per second for Spin), metres for Translate.")] public float minValue, maxValue;
        [TextArea(1, 2)] public string description = "";
        Quaternion restRot; Vector3 restPos; bool cached;

        void Awake() => Cache();
        void Cache() { if (cached) return; restRot = transform.localRotation; restPos = transform.localPosition; cached = true; }

        /// <summary>0 = min, 1 = max. For Spin, use a continuous angle instead (motion is driven by mechanics).</summary>
        public void SetNormalized(float t)
        {
            Cache(); float v = Mathf.Lerp(minValue, maxValue, Mathf.Clamp01(t));
            if (motion == Motion.Translate) transform.localPosition = restPos + localAxis.normalized * v;
            else transform.localRotation = restRot * Quaternion.AngleAxis(v, localAxis);
        }
        public void ResetPose() { Cache(); transform.localPosition = restPos; transform.localRotation = restRot; }
    }
}
