using UnityEngine;

namespace MedSimAI.Hospital
{
    /// <summary>Turns a world label toward the viewer (yaw only).</summary>
    public class FaceCamera : MonoBehaviour
    {
        void LateUpdate()
        {
            var c = Camera.main; if (!c) return;
            var d = transform.position - c.transform.position; d.y = 0;
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
        }
    }
}
