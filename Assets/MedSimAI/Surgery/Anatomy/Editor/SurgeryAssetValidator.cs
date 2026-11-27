using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Anatomy;
using MedSimAI.Surgery.Instruments;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Surgery.AssetsEditor
{
    /// <summary>
    /// Pre-integration checks for every Person 2 prefab (also run by the EditMode tests). Returns human-readable issues;
    /// an empty list means the assets satisfy the contract checks.  Menu: MedSim AI ▸ Surgery ▸ Validate anatomy & instrument assets
    /// </summary>
    public static class SurgeryAssetValidator
    {
        static GameObject Load(string p) => AssetDatabase.LoadAssetAtPath<GameObject>(p);

        [MenuItem("MedSim AI/Surgery/Validate anatomy & instrument assets")]
        public static void Menu() { var issues = ValidateAll(); Debug.Log(issues.Count == 0 ? "[Surgery assets] OK — no issues" : "[Surgery assets] " + issues.Count + " issue(s):\n" + string.Join("\n", issues)); }
        public static string Summary() { var i = ValidateAll(); return i.Count == 0 ? "OK (0 issues)" : i.Count + " issue(s): " + string.Join(" | ", i.Take(12)); }

        public static List<string> ValidateAll()
        {
            var issues = new List<string>();
            foreach (var p in RequiredPrefabs()) { var g = Load(p); if (!g) { issues.Add("missing prefab " + p); continue; } Generic(g, p, issues); }
            if (issues.Any(i => i.StartsWith("missing prefab"))) return issues;
            Knee(Load(SurgeryAssetBuilder.KneePrefab), issues);
            Patient(Load(SurgeryAssetBuilder.PatientPrefab), issues);
            foreach (var id in SurgeryAssetBuilder.AllInstrumentIds) Instrument(id, Load(SurgeryAssetBuilder.InstrumentPrefab(id)), issues);
            foreach (var id in ImplantIds.All) Implant(id, Load(SurgeryAssetBuilder.ImplantPrefab(id)), issues);
            Tray(Load(SurgeryAssetBuilder.TrayPrefab), issues);
            return issues;
        }

        public static IEnumerable<string> RequiredPrefabs()
        {
            yield return SurgeryAssetBuilder.KneePrefab; yield return SurgeryAssetBuilder.PatientPrefab; yield return SurgeryAssetBuilder.DrapePrefab;
            yield return SurgeryAssetBuilder.ImplantSetPrefab; yield return SurgeryAssetBuilder.TrayPrefab; yield return SurgeryAssetBuilder.TrayPopulatedPrefab;
            foreach (var id in SurgeryAssetBuilder.AllInstrumentIds) yield return SurgeryAssetBuilder.InstrumentPrefab(id);
            foreach (var id in ImplantIds.All) yield return SurgeryAssetBuilder.ImplantPrefab(id);
        }

        /// <summary>No missing scripts / meshes / materials; root has unit scale.</summary>
        public static void Generic(GameObject g, string path, List<string> issues)
        {
            foreach (var t in g.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0) issues.Add($"{path}: missing script on {t.name}");
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true)) if (!mf.sharedMesh) issues.Add($"{path}: missing mesh on {mf.name}");
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(m => !m)) issues.Add($"{path}: missing material on {r.name}");
                var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh && mf.sharedMesh.subMeshCount != r.sharedMaterials.Length) issues.Add($"{path}: submesh/material count mismatch on {r.name}");
            }
            if (g.transform.localScale != Vector3.one) issues.Add($"{path}: root scale is not (1,1,1)");
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) if (t.localScale.x < 0 || t.localScale.y < 0 || t.localScale.z < 0) issues.Add($"{path}: negative scale on {t.name}");
        }

        public static void Knee(GameObject k, List<string> issues)
        {
            if (!k.GetComponent<KneeAnatomyStateController>()) issues.Add("knee: no KneeAnatomyStateController on root");
            var views = k.GetComponentsInChildren<AnatomyTargetStateView>(true);
            foreach (var id in AnatomyTargetIds.All)
            {
                var v = views.Where(x => x.TargetId == id).ToArray();
                if (v.Length != 1) { issues.Add($"knee: target {id} found {v.Length}x"); continue; }
                var c = v[0].GetComponent<Collider>(); if (!c || !c.isTrigger) issues.Add($"knee: target {id} needs a trigger collider");
                if (v[0].SupportedStates.Count == 0 || v[0].SupportedStates[0] != AnatomyStates.Intact) issues.Add($"knee: target {id} must start 'intact'");
            }
            var ids = k.GetComponentsInChildren<AnatomyStructure>(true).Select(s => s.structureId).ToList();
            foreach (var d in ids.GroupBy(x => x).Where(x => x.Count() > 1)) issues.Add("knee: duplicate structureId " + d.Key);
            if (ids.Any(string.IsNullOrEmpty)) issues.Add("knee: empty structureId");
            var names = k.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
            foreach (var id in ImplantIds.All) if (!names.Contains("PlacementTarget_" + id)) issues.Add("knee: missing PlacementTarget_" + id);
            foreach (var n in new[] { "KneeRoot", "FemurRoot", "TibiaRoot", "PatellaRoot", "FibulaRoot", "GuideTarget_" + InstrumentIds.FemoralCuttingGuide, "GuideTarget_" + InstrumentIds.TibialCuttingGuide, "PatellaRetractedPose" })
                if (!names.Contains(n)) issues.Add("knee: missing anchor " + n);
            foreach (var req in new[] { "knee_r.bone.distal_femur", "knee_r.bone.proximal_tibia", "knee_r.bone.fibula", "knee_r.bone.patella", "knee_r.cartilage.femoral", "knee_r.cartilage.tibial",
                                        "knee_r.cartilage.patellar", "knee_r.meniscus.medial", "knee_r.meniscus.lateral", "knee_r.ligament.acl", "knee_r.ligament.pcl", "knee_r.ligament.mcl", "knee_r.ligament.lcl", "knee_r.tendon.patellar", "knee_r.tendon.quadriceps" })
                if (!ids.Contains(req)) issues.Add("knee: missing structure " + req);
        }

        public static void Patient(GameObject p, List<string> issues)
        {
            if (p.transform.position != Vector3.zero || p.transform.rotation != Quaternion.identity) issues.Add("patient: root must have a zero pose");
            var site = p.GetComponentsInChildren<SurgeryAnchor>(true).FirstOrDefault(a => a.anchorId == AnchorIds.SurgicalSiteKneeR);
            if (!site) { issues.Add("patient: missing " + AnchorIds.SurgicalSiteKneeR); return; }
            if (site.name != AnchorIds.SurgicalSiteKneeR) issues.Add("patient: site anchor name must equal its id");
            if (site.transform.position.x >= 0) issues.Add("patient: right knee must be at negative X (patient's right = -X)");
            if (!site.GetComponentInChildren<KneeAnatomyStateController>(true)) issues.Add("patient: KneeAnatomy_R must be a child of the surgical site");
            if (!p.GetComponentsInChildren<AnatomyStructure>(true).Any(s => s.category == AnatomyCategory.Drape)) issues.Add("patient: no drape");
            var r = p.GetComponentsInChildren<Renderer>(true).Where(x => x.GetComponent<AnatomyStructure>()?.category == AnatomyCategory.Skin).Select(x => x.bounds).ToList();
            if (r.Count > 0) { var b = r[0]; foreach (var x in r) b.Encapsulate(x); if (b.min.y < -0.03f) issues.Add($"patient: body below the support plane ({b.min.y:0.000} m)"); if (b.size.x > 0.56f) issues.Add($"patient: wider than the table ({b.size.x:0.00} m)"); }
        }

        public static void Instrument(string id, GameObject g, List<string> issues)
        {
            var tag = g.GetComponent<InstrumentTagBridge>(); if (!tag) { issues.Add($"{id}: no InstrumentTagBridge (SurgeryInstrumentTag carrier) on root"); return; }
            if (tag.instrumentId != id) issues.Add($"{id}: tag id '{tag.instrumentId}' does not match");
            if (!tag.tip) issues.Add($"{id}: tip not assigned");
            else if (g.transform.InverseTransformPoint(tag.tip.position).z <= 0.0f) issues.Add($"{id}: tip must lie toward +Z of the grip");
            if (string.IsNullOrEmpty(tag.displayNameEn) || string.IsNullOrEmpty(tag.displayNameAr)) issues.Add($"{id}: display names missing");
            var info = g.GetComponent<InstrumentAssetInfo>(); if (!info) issues.Add($"{id}: no InstrumentAssetInfo");
            else
            {
                if (!info.gripPoint || info.gripPoint.localPosition.sqrMagnitude > 1e-8f) issues.Add($"{id}: GripPoint must sit at the root origin");
                foreach (var t in new[] { info.interactionPoint, info.attachmentPoint, info.resetAnchor }) if (!t) issues.Add($"{id}: missing anchor");
                float len = info.sizeMm.z / 1000f; if (len < 0.05f || len > 0.40f) issues.Add($"{id}: implausible length {len:0.000} m");
            }
            if (!g.GetComponent<Rigidbody>()) issues.Add($"{id}: no Rigidbody");
            if (g.GetComponentsInChildren<Collider>(true).Length == 0) issues.Add($"{id}: no colliders");
            var grab = g.GetComponent<XRGrabInteractable>(); if (!grab) issues.Add($"{id}: no XRGrabInteractable"); else if (!grab.attachTransform) issues.Add($"{id}: grab attachTransform not set");
        }

        public static void Implant(string id, GameObject g, List<string> issues)
        {
            var tag = g.GetComponent<ImplantTagBridge>(); if (!tag || tag.implantId != id) issues.Add($"{id}: ImplantTagBridge (SurgeryImplantTag carrier) missing or wrong id");
            var info = g.GetComponent<ImplantAssetInfo>(); if (!info || !info.placementAnchor || info.placementAnchor.localPosition.sqrMagnitude > 1e-8f) issues.Add($"{id}: PlacementAnchor must be at the root origin");
            var mc = g.GetComponentInChildren<MeshCollider>(true); if (!mc || !mc.convex) issues.Add($"{id}: needs a convex MeshCollider");
            if (!g.GetComponent<Rigidbody>() || !g.GetComponent<XRGrabInteractable>()) issues.Add($"{id}: needs Rigidbody + XRGrabInteractable");
        }

        public static void Tray(GameObject t, List<string> issues)
        {
            var anchors = t.GetComponentsInChildren<SurgeryAnchor>(true).ToDictionary(a => a.anchorId, a => a);
            foreach (var id in SurgeryAssetBuilder.AllInstrumentIds)
                if (!anchors.TryGetValue(AnchorIds.InstrumentSlot(id), out var a) || a.name != a.anchorId) issues.Add("tray: missing slot " + AnchorIds.InstrumentSlot(id));
        }
    }
}
