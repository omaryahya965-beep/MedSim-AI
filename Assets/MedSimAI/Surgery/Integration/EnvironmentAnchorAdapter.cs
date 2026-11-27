using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Integration
{
    /// <summary>
    /// Completes the contract anchors on top of whatever room is in the scene (Person 1's OperatingRoom.prefab or the
    /// placeholder). Room anchors are resolved through SurgeryAnchor.Find (contract 1.1 aliases). Anchors the room does not
    /// define are derived here from the ones it does, so the AI characters always have valid targets:
    ///   OperatingTable ← PatientRoot · AssistantAtInstrumentTable ← in front of InstrumentTable ·
    ///   SurgeonObserve ← opposite side of the table from the student · ImplantTable ← InstrumentTable (until P2/P1 add one)
    /// Nothing in another person's prefab is modified; derived anchors live under "[Integration Anchors]".
    /// </summary>
    public static class EnvironmentAnchorAdapter
    {
        public static string EnsureDerived()
        {
            var log = new System.Text.StringBuilder();
            Transform parent = null;
            Transform Make(string id, Vector3 pos, Vector3 faceTo)
            {
                if (!parent) { var existing = GameObject.Find("[Integration Anchors]"); parent = existing ? existing.transform : new GameObject("[Integration Anchors]").transform; }
                var go = new GameObject(id); go.transform.SetParent(parent, true); go.transform.position = pos;
                var d = faceTo - pos; d.y = 0; if (d.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(d);
                go.AddComponent<SurgeryAnchor>().anchorId = id;
                log.AppendLine("derived " + id);
                return go.transform;
            }
            var patient = SurgeryAnchor.Find(AnchorIds.PatientRoot);
            var site = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR);
            var student = SurgeryAnchor.Find(AnchorIds.StudentOperatingPosition);
            var table = SurgeryAnchor.Find(AnchorIds.InstrumentTable);
            var focus = site ? site.position : patient ? patient.position : Vector3.zero;

            if (!SurgeryAnchor.Find(AnchorIds.OperatingTable) && patient) Make(AnchorIds.OperatingTable, patient.position, patient.position + Vector3.forward);
            if (!SurgeryAnchor.Find(AnchorIds.AssistantAtTable) && table)
            {
                var f = table.forward; f.y = 0; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;   // P1: local +Z points into the room
                var p = table.position + f * 0.55f; p.y = 0; Make(AnchorIds.AssistantAtTable, p, table.position);
            }
            if (!SurgeryAnchor.Find(AnchorIds.SurgeonObserve) && patient)
            {
                // Opposite side of the table from the student (patient's left), slightly toward the head, facing the site.
                float side = student ? -Mathf.Sign(student.position.x - patient.position.x) : 1f;
                var p = new Vector3(patient.position.x + side * 0.95f, 0, focus.z + 0.75f);
                Make(AnchorIds.SurgeonObserve, p, focus);
            }
            if (!SurgeryAnchor.Find(AnchorIds.ImplantTable) && table) Make(AnchorIds.ImplantTable, table.position, focus);
            if (!SurgeryAnchor.Find(AnchorIds.StudentOperatingPosition) && patient)
            {
                var p = new Vector3(patient.position.x - 0.75f, 0, focus.z);   // patient's right = world −X (contract §4)
                Make(AnchorIds.StudentOperatingPosition, p, focus);
            }
            return log.ToString();
        }
    }
}
