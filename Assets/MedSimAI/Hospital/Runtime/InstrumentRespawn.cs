using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Keeps instruments and implant components usable: they rest kinematically in their tray slot until first taken; once
    /// released they fall naturally (gravity). If one drops below table height (the floor) or is left lying away from its
    /// slot for a while, it goes back to its own slot. A component seated on the knee by the mechanics (kinematic, parented to
    /// its placement target) is left where it is. Nothing falls forever, floats in mid-air or disappears.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class InstrumentRespawn : MonoBehaviour
    {
        public float floorY = 0.35f, restSeconds = 25f, slotRadius = 0.6f;
        Transform parent0; Vector3 localPos0, worldPos0; Quaternion localRot0; bool kinematic0;
        Rigidbody rb; XRGrabInteractable grab; float releasedAt = -1f;

        void Awake()
        {
            rb = GetComponent<Rigidbody>(); grab = GetComponent<XRGrabInteractable>();
            parent0 = transform.parent; localPos0 = transform.localPosition; localRot0 = transform.localRotation; worldPos0 = transform.position; kinematic0 = rb.isKinematic;
            if (grab) { grab.selectEntered.AddListener(_ => releasedAt = -1f); grab.selectExited.AddListener(_ => StartCoroutine(AfterRelease())); }
        }

        IEnumerator AfterRelease()
        {
            yield return null;   // let XRI and the mechanics handle the release first (placement may seat it)
            if (rb.isKinematic && transform.parent != parent0) yield break;   // seated on the knee
            rb.isKinematic = false; rb.useGravity = true; releasedAt = Time.time;
        }

        bool Held => grab && grab.isSelected;

        void Update()
        {
            if (Held) { releasedAt = -1f; return; }
            if (rb.isKinematic) return;
            if (transform.position.y < floorY) { ReturnToSlot(); return; }
            if (releasedAt < 0f) releasedAt = Time.time;   // e.g. dropped from the desktop hand
            if (Time.time - releasedAt > restSeconds && rb.IsSleeping() && Vector3.Distance(transform.position, worldPos0) > slotRadius) ReturnToSlot();
        }

        /// <summary>Back to its own slot, at rest.</summary>
        public void ReturnToSlot()
        {
            rb.isKinematic = true;
            transform.SetParent(parent0, false); transform.localPosition = localPos0; transform.localRotation = localRot0;
            rb.isKinematic = kinematic0; releasedAt = -1f;
        }
    }
}
