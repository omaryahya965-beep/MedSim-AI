using System.Collections;
using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Guided training only: a soft pulsing ring marks the anatomy target of the current objective (from the authored
    /// scenario), so the student knows where to work. It is a visual cue; it never completes or validates anything.
    /// Hidden in independent assessment.
    /// </summary>
    public class ObjectiveHighlighter : MonoBehaviour
    {
        public Transform ring;            // built by the scene builder (thin torus, unlit translucent)
        public float pulseHz = 0.8f;
        SurgicalScenarioData scenario; SurgeryEventBus bus; bool on; Transform target; Vector3 baseScale = Vector3.one;

        void Awake()
        {
            var ta = Resources.Load<TextAsset>("SurgeryScenarios/" + SurgeryContract.SCENARIO_ID_TKA);
            if (ta) scenario = JsonUtility.FromJson<SurgicalScenarioData>(ta.text);
            if (ring) { baseScale = ring.localScale; ring.gameObject.SetActive(false); }
        }
        void OnEnable() => StartCoroutine(Hook());
        void OnDisable() { if (bus != null) bus.EventPublished -= OnEvent; bus = null; }
        IEnumerator Hook() { while (!SurgeryEventBusHost.Instance) yield return null; bus = SurgeryEventBusHost.Instance.Bus; bus.EventPublished += OnEvent; }

        public void SetEnabled(bool enabledNow) { on = enabledNow; Refresh(); }

        void OnEvent(SurgeryEvent e)
        {
            if (e.source != SurgeryEventSources.Mechanics || e.id != SurgeryEventIds.ObjectiveStarted || scenario == null) return;
            var o = scenario.objectives.FirstOrDefault(x => x.id == e.objectiveId);
            var id = o != null && o.targets != null && o.targets.Length > 0 ? o.targets[0] : null;
            target = id == null ? null : FindObjectsByType<SurgeryAnatomyTag>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.targetId == id)?.transform;
            Refresh();
        }

        void Refresh() { if (ring) ring.gameObject.SetActive(on && target); }

        void Update()
        {
            if (!ring || !on || !target) return;
            var b = target.GetComponent<Collider>();
            ring.position = b ? b.bounds.center : target.position;
            float s = 1f + 0.12f * Mathf.Sin(Time.time * Mathf.PI * 2f * pulseHz);
            ring.localScale = baseScale * s;
        }
    }
}
