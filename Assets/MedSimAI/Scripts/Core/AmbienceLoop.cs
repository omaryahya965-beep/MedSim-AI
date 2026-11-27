using UnityEngine;

namespace MedSimAI.Audio
{
    /// <summary>Plays the procedurally generated, quiet ventilation bed on loop (no music during the clinical case).</summary>
    [RequireComponent(typeof(AudioSource))]
    public class AmbienceLoop : MonoBehaviour
    {
        void Start()
        {
            var a = GetComponent<AudioSource>();
            a.clip = ProceduralClinicalAudio.Ventilation(); a.loop = true; a.Play();
        }
    }
}
