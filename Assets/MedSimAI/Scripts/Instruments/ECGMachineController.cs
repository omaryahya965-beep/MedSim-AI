using System.Collections;
using System.Collections.Generic;
using MedSimAI.Audio;
using MedSimAI.Clinical;
using MedSimAI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Instruments
{
    /// <summary>ECG cart: press "Record" to request the scenario-defined 12-lead; grabbing or viewing the printout records ECG_REVIEWED.</summary>
    public class ECGMachineController : MonoBehaviour
    {
        public XRSimpleInteractable recordButton;
        public Renderer screen;
        public GameObject printout; public Renderer printoutRenderer;
        public TMP_Text statusText;
        public AudioSource sfx; public AudioClip printClip;
        bool recorded;

        void Awake()
        {
            if (recordButton) recordButton.selectEntered.AddListener(_ => Record());
            if (printout)
            {
                var g = printout.GetComponent<XRGrabInteractable>();
                if (g) g.selectEntered.AddListener(_ => MarkReviewed());
                printout.SetActive(false);
            }
        }

        public void Record()
        {
            if (recorded) { MarkReviewed(); return; }
            StartCoroutine(RecordRoutine());
        }

        IEnumerator RecordRoutine()
        {
            var r = ClinicalScenarioManager.Instance.Session.RequestInvestigation("INV_ECG");
            if (!r.success || !r.available) { if (statusText) statusText.text = r.result; yield break; }
            ShowResult(r);
            yield return null;
        }

        public void ShowResult(InvestigationResult r)
        {
            recorded = true;
            var tex = ECGTracingRenderer.Render(r.imageKey, ClinicalScenarioManager.Instance.Session.Patient.Vitals.heartRate);
            if (screen) screen.material.SetTexture("_BaseMap", tex);
            if (screen) screen.material.SetTexture("_EmissionMap", tex);
            if (printoutRenderer) printoutRenderer.material.SetTexture("_BaseMap", tex);
            if (printout) printout.SetActive(true);
            if (sfx && printClip) sfx.PlayOneShot(printClip);
            if (statusText) statusText.text = "12-LEAD RECORDED · SIMULATED EDUCATIONAL TRACING";
            ClinicalFeedbackBus.Publish("12-lead ECG", r.result);
        }

        public void MarkReviewed()
        {
            var s = ClinicalScenarioManager.Instance.Session;
            if (recorded && !s.Tracker.Has(ClinicalEvents.EcgReviewed)) s.Record(ClinicalEvents.EcgReviewed);
        }

        public void ResetMachine()
        {
            recorded = false; if (printout) printout.SetActive(false);
            if (statusText) statusText.text = "READY · Press RECORD";
            if (screen) { screen.material.SetTexture("_BaseMap", null); screen.material.SetTexture("_EmissionMap", null); }
        }
    }
}
