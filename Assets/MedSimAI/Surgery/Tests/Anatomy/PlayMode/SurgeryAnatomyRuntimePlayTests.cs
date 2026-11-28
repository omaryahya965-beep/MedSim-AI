using System.Collections;
using System.Linq;
using MedSimAI.Surgery.Anatomy;
using MedSimAI.Surgery.Instruments;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MedSimAI.Surgery.Tests.Anatomy
{
    /// <summary>Runtime behaviour of the Person 2 prefabs (instantiated in play mode; no scene, no network).</summary>
    public class SurgeryAnatomyRuntimePlayTests
    {
        static GameObject Load(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }
        const string R = "Assets/MedSimAI/Surgery/";
        GameObject root;
        [TearDown] public void Cleanup() { if (root) Object.Destroy(root); }

        [UnityTest] public IEnumerator Patient_AwakeCreatesContractTags_AndSiteAnchorResolves()
        {
            root = Object.Instantiate(Load(R + "Anatomy/Prefabs/SurgicalPatient.prefab")); yield return null;
            var tags = root.GetComponentsInChildren<SurgeryAnatomyTag>(true).Where(t => t.gameObject.activeInHierarchy).Select(t => t.targetId).ToList();
            foreach (var id in AnatomyTargetIds.All) CollectionAssert.Contains(tags, id, "runtime SurgeryAnatomyTag for " + id);
            var site = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR);
            Assert.IsNotNull(site); Assert.IsTrue(site.IsChildOf(root.transform));
        }

        [UnityTest] public IEnumerator Instrument_AwakeCreatesSurgeryInstrumentTag()
        {
            root = Object.Instantiate(Load(R + "Instruments/Prefabs/inst.oscillating_saw.prefab")); yield return null;
            Assert.AreEqual(1, root.GetComponents<SurgeryInstrumentTag>().Length, "no duplicate tag at runtime");
            var tag = root.GetComponent<SurgeryInstrumentTag>();
            Assert.IsNotNull(tag); Assert.AreEqual(InstrumentIds.OscillatingSaw, tag.instrumentId); Assert.IsNotNull(tag.tip);
            var blade = root.GetComponentsInChildren<InstrumentMovablePart>().First(p => p.partId == "Pivot_Blade");
            var q0 = blade.transform.localRotation; blade.SetNormalized(1); Assert.Greater(Quaternion.Angle(q0, blade.transform.localRotation), 3.5f); blade.ResetPose();
        }

        [UnityTest] public IEnumerator PatellaRetraction_BlendsOverTime_ThenRestores()
        {
            root = Object.Instantiate(Load(R + "Anatomy/Prefabs/KneeAnatomy_R.prefab")); yield return null;
            var c = root.GetComponent<KneeAnatomyStateController>(); var rest = c.patellaGroup.localPosition;
            Assert.IsTrue(c.GetTarget(AnatomyTargetIds.JointCapsule).SetState(AnatomyStates.Exposed));
            yield return null;
            Assert.Greater(Vector3.Distance(c.patellaGroup.localPosition, rest), 0f, "started moving");
            Assert.Greater(Vector3.Distance(c.patellaGroup.localPosition, c.patellaRetractedPose.localPosition), 1e-4f, "not instant");
            float t = 0; while (t < c.patellaBlendSeconds + 0.3f) { t += Time.deltaTime; yield return null; }
            Assert.Less(Vector3.Distance(c.patellaGroup.localPosition, c.patellaRetractedPose.localPosition), 1e-4f, "reached the retracted pose");
            c.ResetAll(); Assert.Less(Vector3.Distance(c.patellaGroup.localPosition, rest), 1e-4f);
        }

        [UnityTest] public IEnumerator Implant_AwakeCreatesTag_AndCanBeGivenItsPlacementTarget()
        {
            root = new GameObject("test");
            var knee = Object.Instantiate(Load(R + "Anatomy/Prefabs/KneeAnatomy_R.prefab"), root.transform);
            var imp = Object.Instantiate(Load(R + "Instruments/Prefabs/Implants/implant.femoral_component.prefab"), root.transform); yield return null;
            var tags = imp.GetComponents<SurgeryImplantTag>(); Assert.AreEqual(1, tags.Length, "one serialized tag, no runtime duplicate");
            var tag = tags[0]; tag.placementTarget = knee.GetComponentsInChildren<Transform>(true).First(x => x.name == "PlacementTarget_" + ImplantIds.FemoralComponent);
            Assert.AreEqual(ImplantIds.FemoralComponent, tag.implantId); Assert.IsNotNull(tag.placementTarget);
        }
    }
}
