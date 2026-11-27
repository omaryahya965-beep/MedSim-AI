using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Automatic sliding doors for Person 1's operating-room entrance (DoorLeafLeft / DoorLeafRight, each with a collider
    /// and a carving NavMeshObstacle). The leaves slide ±travel along their local X when the student or a character is
    /// near the doorway, and close again after everyone has gone. Like a real OR motion sensor; no button needed.
    /// </summary>
    public class AutoSlidingDoor : MonoBehaviour
    {
        public Transform leafLeft, leafRight;
        public float travel = 0.91f, openSeconds = 1.1f, sensorRadius = 2.1f, holdOpenSeconds = 1.6f;
        public bool IsOpen => open01 > 0.95f;
        public bool IsClosed => open01 < 0.02f;

        Vector3 closedL, closedR; float open01, lastPresence = -100f, nextScan;
        readonly List<Transform> sensed = new List<Transform>();

        void Awake()
        {
            if (leafLeft) closedL = leafLeft.localPosition;
            if (leafRight) closedR = leafRight.localPosition;
            foreach (var leaf in new[] { leafLeft, leafRight })
            {
                if (!leaf) continue;
                var obstacle = leaf.GetComponentInChildren<NavMeshObstacle>();
                if (obstacle) obstacle.carveOnlyStationary = false;   // the opening appears/disappears as the leaves move
            }
        }

        void Scan()
        {
            sensed.Clear();
            if (Camera.main) sensed.Add(Camera.main.transform);
            foreach (var a in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None)) sensed.Add(a.transform);
        }

        void Update()
        {
            if (Time.time >= nextScan) { nextScan = Time.time + 1.5f; Scan(); }
            var c = transform.position;
            foreach (var t in sensed)
            {
                if (!t) continue;
                var p = t.position; p.y = c.y;
                if (Vector3.Distance(p, c) <= sensorRadius) { lastPresence = Time.time; break; }
            }
            float target = Time.time - lastPresence <= holdOpenSeconds ? 1f : 0f;
            open01 = Mathf.MoveTowards(open01, target, Time.deltaTime / Mathf.Max(0.1f, openSeconds));
            float e = open01 * open01 * (3f - 2f * open01);   // smoothstep
            if (leafLeft) leafLeft.localPosition = closedL + Vector3.left * travel * e;
            if (leafRight) leafRight.localPosition = closedR + Vector3.right * travel * e;
        }
    }
}
