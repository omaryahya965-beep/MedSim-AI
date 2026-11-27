using System;
using MedSimAI.Clinical;
using MedSimAI.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MedSimAI.Instruments
{
    /// <summary>A body region that accepts specific instruments. Placed as trigger colliders on the patient.</summary>
    [RequireComponent(typeof(Collider))]
    public class PlacementZone : MonoBehaviour
    {
        public string zoneId;
        public string displayName;
        public string[] acceptedInstruments;
        [Tooltip("Where attachable instruments snap to.")] public Transform snapPoint;
        [Tooltip("If set, the instrument's alignment axis must be within this angle of the zone's forward.")] public float maxAngle = 75f;
        public Renderer highlight;
        static readonly Color Ok = new Color(0.1f, 0.85f, 0.75f, 0.35f), Hover = new Color(1f, 1f, 1f, 0.15f);
        MaterialPropertyBlock mpb;
        public bool Occupied { get; set; }

        void Awake() { GetComponent<Collider>().isTrigger = true; if (highlight) highlight.enabled = false; }

        public bool Accepts(string instrument) => Array.IndexOf(acceptedInstruments, instrument) >= 0;

        public void SetHighlight(bool on, bool valid)
        {
            if (!highlight) return;
            highlight.enabled = on;
            mpb ??= new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", valid ? Ok : Hover);
            highlight.SetPropertyBlock(mpb);
        }
    }
}
