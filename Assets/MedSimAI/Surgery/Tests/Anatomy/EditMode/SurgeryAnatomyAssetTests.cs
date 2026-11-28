using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedSimAI.Surgery.Anatomy;
using MedSimAI.Surgery.AssetsEditor;
using MedSimAI.Surgery.Instruments;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Surgery.Tests.Anatomy
{
    /// <summary>Person 2 asset contract checks (no scene, no network, no API).</summary>
    public class SurgeryAnatomyAssetTests
    {
        static GameObject P(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
        readonly List<Object> spawned = new List<Object>();
        GameObject Spawn(string path) { var g = (GameObject)Object.Instantiate(P(path)); spawned.Add(g); return g; }
        [TearDown] public void Cleanup() { foreach (var o in spawned) if (o) Object.DestroyImmediate(o); spawned.Clear(); }

        [Test] public void Validator_ReportsNoIssues()
        {
            var issues = SurgeryAssetValidator.ValidateAll();
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }

        [Test] public void RequiredPrefabs_Exist() { foreach (var p in SurgeryAssetValidator.RequiredPrefabs()) Assert.IsNotNull(P(p), p); }

        [Test] public void KneeTargets_CoverContractAndScenarioStates()
        {
            var scen = SimpleJson.Obj(SimpleJson.Parse(File.ReadAllText(SurgeryAssetBuilder.ScenarioPath)));
            var views = P(SurgeryAssetBuilder.KneePrefab).GetComponentsInChildren<AnatomyTargetStateView>(true).ToDictionary(v => v.TargetId);
            CollectionAssert.AreEquivalent(AnatomyTargetIds.All, views.Keys);
            foreach (var t in SimpleJson.Arr(scen["targets"]).Select(SimpleJson.Obj))
            {
                var states = SimpleJson.Arr(t["states"]).Cast<string>().ToArray();
                Assert.IsTrue(views.ContainsKey((string)t["id"]), (string)t["id"]);
                CollectionAssert.IsSubsetOf(states, views[(string)t["id"]].SupportedStates.ToArray(), (string)t["id"]);
            }
            foreach (var v in views.Values) { Assert.IsTrue(v is IAnatomyStateView); Assert.IsTrue(v.GetComponent<Collider>().isTrigger, v.TargetId); }
        }

        [Test] public void StructureIds_AreUnique_AndKeyStructuresPresent()
        {
            var ids = P(SurgeryAssetBuilder.KneePrefab).GetComponentsInChildren<AnatomyStructure>(true).Select(s => s.structureId).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "duplicate structureId");
            foreach (var k in new[] { "knee_r.bone.distal_femur", "knee_r.bone.proximal_tibia", "knee_r.bone.fibula", "knee_r.bone.patella", "knee_r.cartilage.femoral",
                                      "knee_r.meniscus.medial", "knee_r.meniscus.lateral", "knee_r.ligament.acl", "knee_r.ligament.pcl", "knee_r.ligament.mcl", "knee_r.ligament.lcl" })
                CollectionAssert.Contains(ids, k);
        }

        [Test] public void StateTransitions_SwapVisuals_AndResetRestoresIntact()
        {
            var k = Spawn(SurgeryAssetBuilder.KneePrefab); var c = k.GetComponent<KneeAnatomyStateController>();
            c.Refresh(true);
            Assert.IsTrue(c.skinIntact.activeSelf); Assert.IsFalse(c.skinOpen.activeSelf); Assert.IsTrue(c.distalFemur.activeSelf); Assert.IsFalse(c.distalFemurResected.activeSelf);
            Assert.IsTrue(c.SetTargetState(AnatomyTargetIds.SiteMarking, AnatomyStates.Marked)); Assert.IsTrue(c.siteMarking.activeSelf);
            Assert.IsTrue(c.SetTargetState(AnatomyTargetIds.IncisionLine, AnatomyStates.Incised)); Assert.IsTrue(c.incisionLine.activeSelf); Assert.IsFalse(c.siteMarking.activeSelf);
            Assert.IsTrue(c.SetTargetState(AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed)); Assert.IsTrue(c.skinOpen.activeSelf); Assert.IsFalse(c.skinIntact.activeSelf);
            foreach (var g in c.hiddenWhenExposed) Assert.IsFalse(g.activeSelf, g.name + " hidden while exposed");
            Assert.IsTrue(c.SetTargetState(AnatomyTargetIds.DistalFemur, AnatomyStates.GuidePlaced)); Assert.IsTrue(c.guidePinsFemur.activeSelf);
            Assert.IsTrue(c.SetTargetState(AnatomyTargetIds.DistalFemur, AnatomyStates.Resected)); Assert.IsTrue(c.distalFemurResected.activeSelf); Assert.IsFalse(c.distalFemur.activeSelf); Assert.IsFalse(c.femoralCartilage.activeSelf); Assert.IsFalse(c.guidePinsFemur.activeSelf);
            Assert.IsTrue(c.SetTargetState(AnatomyTargetIds.ProximalTibia, AnatomyStates.Resected)); foreach (var g in c.removedWithTibialResection) Assert.IsFalse(g.activeSelf, g.name);
            c.showComponentVisuals = true;
            Assert.IsTrue(c.SetTargetState(AnatomyTargetIds.DistalFemur, AnatomyStates.Implanted)); Assert.IsTrue(c.femoralComponentVisual.activeSelf);
            Assert.IsTrue(c.SetTargetState(AnatomyTargetIds.Wound, AnatomyStates.Closed)); Assert.IsTrue(c.skinStaples.activeSelf); Assert.IsTrue(c.skinIntact.activeSelf); Assert.IsFalse(c.skinOpen.activeSelf);
            c.ResetAll();
            foreach (var v in c.Targets.Values) Assert.AreEqual(AnatomyStates.Intact, v.CurrentState, v.TargetId);
            Assert.IsTrue(c.skinIntact.activeSelf); Assert.IsTrue(c.distalFemur.activeSelf); Assert.IsFalse(c.skinStaples.activeSelf);
        }

        [Test] public void EnclosedAnatomy_IsCulledWhileSkinIsClosed_CollidersStayActive()
        {
            var k = Spawn(SurgeryAssetBuilder.KneePrefab); var c = k.GetComponent<KneeAnatomyStateController>(); c.Refresh(true);
            var bone = c.distalFemur.GetComponent<Renderer>();
            Assert.IsTrue(c.AnatomyEnclosed); Assert.IsTrue(bone.forceRenderingOff, "deep anatomy not rendered under intact skin");
            Assert.IsTrue(c.distalFemur.GetComponent<Collider>().enabled, "colliders remain for mechanics");
            c.SetTargetState(AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed);
            Assert.IsFalse(bone.forceRenderingOff, "rendered once the joint is exposed");
            c.ResetAll(); Assert.IsTrue(bone.forceRenderingOff);
            c.skinIntact.GetComponent<Renderer>().enabled = false; c.ApplyEnclosureCulling(); Assert.IsFalse(bone.forceRenderingOff, "skin hidden by a tool -> anatomy visible");
        }

        [Test] public void UnsupportedState_IsRefused_AndChangesNothing()
        {
            var k = Spawn(SurgeryAssetBuilder.KneePrefab); var c = k.GetComponent<KneeAnatomyStateController>(); c.Refresh(true);
            var v = c.GetTarget(AnatomyTargetIds.Wound);
            Assert.IsFalse(v.SetState(AnatomyStates.Resected)); Assert.IsFalse(v.SetState("")); Assert.AreEqual(AnatomyStates.Intact, v.CurrentState);
            Assert.IsTrue(c.skinIntact.activeSelf);
        }

        [Test] public void ExposedState_RetractsPatellaGroup_ToItsPose()
        {
            var k = Spawn(SurgeryAssetBuilder.KneePrefab); var c = k.GetComponent<KneeAnatomyStateController>(); c.Refresh(true);
            var rest = c.patellaGroup.localPosition;
            c.SetTargetState(AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed); c.Refresh(true);
            Assert.Less(Vector3.Distance(c.patellaGroup.localPosition, c.patellaRetractedPose.localPosition), 1e-4f);
            c.ResetAll(); Assert.Less(Vector3.Distance(c.patellaGroup.localPosition, rest), 1e-4f);
        }

        /// <summary>After the shared-file split the contract tags are saved in the prefab assets themselves (no runtime bridge).</summary>
        [Test] public void ContractTags_AreSerializedInPrefabAssets_ExactlyOnce()
        {
            var knee = P(SurgeryAssetBuilder.KneePrefab);
            foreach (var v in knee.GetComponentsInChildren<AnatomyTargetStateView>(true))
            {
                var tags = v.GetComponents<SurgeryAnatomyTag>();
                Assert.AreEqual(1, tags.Length, v.TargetId); Assert.AreEqual(v.TargetId, tags[0].targetId);
            }
            foreach (var id in SurgeryAssetBuilder.AllInstrumentIds)
            {
                var g = P(SurgeryAssetBuilder.InstrumentPrefab(id)); var t = g.GetComponents<SurgeryInstrumentTag>();
                Assert.AreEqual(1, t.Length, id); Assert.AreEqual(id, t[0].instrumentId); Assert.IsNotNull(t[0].tip, id); Assert.IsNotEmpty(t[0].displayNameAr, id);
                Assert.AreEqual(1, g.GetComponents<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>().Length, id + ": one grab interactable");
                Assert.AreEqual(1, g.GetComponentsInChildren<Rigidbody>(true).Length, id + ": one rigidbody");
            }
            foreach (var id in ImplantIds.All) { var t = P(SurgeryAssetBuilder.ImplantPrefab(id)).GetComponents<SurgeryImplantTag>(); Assert.AreEqual(1, t.Length, id); Assert.AreEqual(id, t[0].implantId); }
            var left = P(SurgeryAssetBuilder.PatientPrefab).GetComponentsInChildren<SurgeryAnatomyTag>(true).Where(t => t.targetId == "knee_l.site_marking").ToList();
            Assert.AreEqual(1, left.Count, "wrong-side trigger on the left knee"); Assert.IsTrue(left[0].GetComponent<BoxCollider>().isTrigger);
        }

        [Test] public void Instruments_FollowPrefabConventions()
        {
            var ids = SurgeryAssetBuilder.AllInstrumentIds.ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
            foreach (var id in ids)
            {
                var g = P(SurgeryAssetBuilder.InstrumentPrefab(id)); var tag = g.GetComponent<SurgeryInstrumentTag>(); var info = g.GetComponent<InstrumentAssetInfo>();
                Assert.AreEqual(id, tag.instrumentId);
                Assert.AreEqual(Vector3.one, g.transform.localScale, id);
                Assert.Less(info.gripPoint.localPosition.magnitude, 1e-5f, id + ": grip at origin");
                var tip = g.transform.InverseTransformPoint(tag.tip.position); Assert.Greater(tip.z, 0.02f, id + ": tip toward +Z");
                Assert.That(info.sizeMm.z, Is.InRange(50f, 400f), id + ": length (mm)");
                Assert.IsNotNull(g.GetComponent<Rigidbody>(), id); Assert.Greater(g.GetComponentsInChildren<Collider>(true).Length, 0, id);
                Assert.AreSame(info.gripPoint, g.GetComponent<XRGrabInteractable>().attachTransform, id);
                Assert.AreEqual(SurgeryAssetBuilder.ExtraInstruments.Contains(id), info.proposedIdNotInContract, id);
            }
            Assert.That(P(SurgeryAssetBuilder.InstrumentPrefab(InstrumentIds.Scalpel)).GetComponent<InstrumentAssetInfo>().sizeMm.z, Is.InRange(130f, 190f), "scalpel ≈ 0.15 m (contract §4)");
        }

        [Test] public void Implants_SeatExactlyOnTheirPlacementTargets()
        {
            var knee = Spawn(SurgeryAssetBuilder.KneePrefab); var c = knee.GetComponent<KneeAnatomyStateController>();
            var visuals = new Dictionary<string, GameObject> { { ImplantIds.FemoralComponent, c.femoralComponentVisual }, { ImplantIds.FemoralTrial, c.femoralTrialVisual },
                { ImplantIds.TibialTray, c.tibialTrayVisual }, { ImplantIds.TibialTrial, c.tibialTrialVisual }, { ImplantIds.PolyInsert, c.polyInsertVisual } };
            foreach (var kv in visuals)
            {
                var target = knee.GetComponentsInChildren<Transform>(true).First(t => t.name == "PlacementTarget_" + kv.Key);
                var imp = Spawn(SurgeryAssetBuilder.ImplantPrefab(kv.Key)); var place = imp.GetComponent<ImplantAssetInfo>().placementAnchor;
                Assert.Less(place.localPosition.magnitude, 1e-5f, kv.Key);
                imp.transform.SetPositionAndRotation(target.position, target.rotation);
                kv.Value.SetActive(true);
                // orientation-invariant comparison: vertex centroid + the farthest vertex from it, both in world space
                (Vector3 c, Vector3 far) Sig(MeshFilter mf)
                {
                    var w = mf.sharedMesh.vertices.Select(v => mf.transform.TransformPoint(v)).ToArray(); var cen = w.Aggregate(Vector3.zero, (s, v) => s + v) / w.Length;
                    return (cen, w.OrderByDescending(v => (v - cen).sqrMagnitude).First());
                }
                var a = Sig(imp.GetComponentInChildren<MeshFilter>()); var b = Sig(kv.Value.GetComponent<MeshFilter>());
                Assert.Less(Vector3.Distance(a.c, b.c), 0.001f, kv.Key + ": seated pose matches the authored component (frame consistency)");
                Assert.Less(Vector3.Distance(a.far, b.far), 0.001f, kv.Key + ": same orientation");
            }
        }

        [Test] public void Patient_OrientationSideAndFit()
        {
            var p = P(SurgeryAssetBuilder.PatientPrefab); var info = p.GetComponent<SurgicalPatientInfo>();
            Assert.AreEqual("R", info.operativeSide);
            Assert.Less(info.surgicalSite.position.x, 0, "operative knee on the patient's right (-X)");
            Transform J(string n) => p.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
            Assert.Greater(J("Joint_HeadTop").position.z, J("Joint_Hip_R").position.z + 0.6f, "head toward +Z");
            Assert.Less(J("Joint_Ankle_L").position.z, J("Joint_Hip_L").position.z - 0.6f, "feet toward -Z");
            Assert.Less(J("Joint_Hip_R").position.x, 0); Assert.Greater(J("Joint_Hip_L").position.x, 0);
            Assert.Less(Vector3.Distance(J("Joint_Knee_R").position, info.surgicalSite.position), 0.02f, "site = right knee joint");
            Assert.AreSame(info.surgicalSite, info.kneeAnatomy.parent);
            Assert.That(info.bodyWidthMeters, Is.LessThan(0.56f), "fits a 0.51 m deck (+ arms tucked)");
        }

        [Test] public void Tray_HasSlots_AndPopulatedInstrumentsDoNotOverlap()
        {
            var tray = P(SurgeryAssetBuilder.TrayPopulatedPrefab); var insts = tray.GetComponentsInChildren<InstrumentAssetInfo>(true);
            Assert.AreEqual(SurgeryAssetBuilder.AllInstrumentIds.Count(), insts.Length);
            var boxes = insts.Select(i => { var r = i.GetComponentsInChildren<Renderer>(true); var b = r[0].bounds; foreach (var x in r) b.Encapsulate(x.bounds); b.Expand(-0.004f); return (i.name, b); }).ToList();
            for (int a = 0; a < boxes.Count; a++) for (int b = a + 1; b < boxes.Count; b++) Assert.IsFalse(boxes[a].b.Intersects(boxes[b].b), $"{boxes[a].name} overlaps {boxes[b].name}");
        }

        [Test] public void AllMaterials_AreUrpLit()
        {
            foreach (var path in SurgeryAssetValidator.RequiredPrefabs())
                foreach (var r in P(path).GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, $"{path}: {r.name}");
        }
    }
}
