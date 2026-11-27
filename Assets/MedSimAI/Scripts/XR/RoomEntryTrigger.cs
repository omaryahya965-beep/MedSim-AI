using MedSimAI.Core;
using UnityEngine;

namespace MedSimAI.Instruments
{
    /// <summary>Trigger volume just inside the examination-room doorway. Records ENTERED_ROOM when the student's head passes.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class RoomEntryTrigger : MonoBehaviour
    {
        public Transform playerHead;
        BoxCollider box; bool done;
        void Awake() { box = GetComponent<BoxCollider>(); box.isTrigger = true; }
        void Update()
        {
            if (done || !playerHead || SessionManager.Instance == null) return;
            var local = transform.InverseTransformPoint(playerHead.position) - box.center;
            var h = box.size * 0.5f;
            if (Mathf.Abs(local.x) <= h.x && Mathf.Abs(local.z) <= h.z) { done = true; SessionManager.Instance.NotifyStudentEnteredRoom(); }
        }
        public void ResetTrigger() => done = false;
    }
}
