using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Voice;
using TMPro;
using UnityEngine;

namespace MedSimAI.UI
{
    /// <summary>
    /// Small diegetic status tag above the character being addressed: Listening (red) / Thinking (blue) / Speaking (teal).
    /// Hidden when idle, so it never clutters the view.
    /// </summary>
    public class ConversationIndicator : MonoBehaviour
    {
        public CharacterSpeechController speaker;
        public Transform head; public Transform viewer;
        TextMeshPro label; float pulse;

        public static ConversationIndicator Create(CharacterSpeechController sp, Transform head, Transform viewer, TMP_FontAsset font)
        {
            var go = new GameObject("Conversation Indicator " + sp.persona);
            var ci = go.AddComponent<ConversationIndicator>(); ci.speaker = sp; ci.head = head; ci.viewer = viewer;
            ci.label = go.AddComponent<TextMeshPro>(); if (font) ci.label.font = font;
            ci.label.fontSize = 0.6f; ci.label.alignment = TextAlignmentOptions.Center; ci.label.rectTransform.sizeDelta = new Vector2(0.5f, 0.08f);
            return ci;
        }

        void LateUpdate()
        {
            var v = VoiceInteractionManager.Instance; if (!v || !head) return;
            string txt = ""; Color c = Color.white;
            bool targeted = v.Target == speaker.persona;
            if (speaker.IsSpeaking) { txt = "• Speaking"; c = new Color(0.39f, 1f, 0.85f); }
            else if (targeted && v.State == VoiceState.Listening) { txt = "• Listening"; c = new Color(1f, 0.45f, 0.4f); }
            else if (targeted && (v.State == VoiceState.Transcribing || v.State == VoiceState.Thinking)) { txt = "• Thinking…"; c = new Color(0.45f, 0.7f, 1f); }
            pulse += Time.deltaTime * 4f;
            c.a = txt.Length == 0 ? 0 : 0.75f + 0.25f * Mathf.Sin(pulse);
            label.text = txt; label.color = c;
            transform.position = head.position + Vector3.up * 0.28f;
            if (viewer) transform.rotation = Quaternion.LookRotation(transform.position - viewer.position, Vector3.up);
        }
    }
}
