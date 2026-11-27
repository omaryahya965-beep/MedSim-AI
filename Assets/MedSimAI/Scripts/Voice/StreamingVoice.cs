using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MedSimAI.Voice
{
    /// <summary>
    /// Plays runtime-generated speech WHILE it is still arriving: PCM16 24 kHz mono bytes are pushed into a ring buffer
    /// that feeds a streaming AudioClip. The AudioSource output drives uLipSync exactly like a normal clip.
    /// </summary>
    public class StreamingVoice
    {
        public const int Rate = 24000;
        readonly float[] ring = new float[Rate * 40];
        readonly object gate = new object();
        long written, consumed, playedAudible, outPos, lastDataOutPos, firstDataOutPos = -1;
        int carry = -1;
        public AudioClip Clip { get; }
        public bool Completed { get; private set; }
        public bool Cancelled { get; private set; }
        public float FirstAudioTime { get; private set; } = -1;
        public float StartTime { get; } = Time.realtimeSinceStartup;
        public float BufferedSeconds { get { lock (gate) return (written - consumed) / (float)Rate; } }
        public float ReceivedSeconds { get { lock (gate) return written / (float)Rate; } }
        /// <summary>Everything received has been played (and the stream has ended).</summary>
        public bool Drained { get { lock (gate) return (Completed || Cancelled) && playedAudible >= written; } }

        public StreamingVoice(string name = "live_stream")
        {
            Clip = AudioClip.Create(name, Rate * 300, 1, Rate, true, OnRead);
        }

        public void PushPcm16(byte[] data, int offset, int count)
        {
            lock (gate)
            {
                int i = offset, end = offset + count;
                if (carry >= 0 && i < end) { Write((short)(carry | (data[i] << 8))); carry = -1; i++; }
                for (; i + 1 < end; i += 2) Write((short)(data[i] | (data[i + 1] << 8)));
                if (i < end) carry = data[i];
            }
            if (FirstAudioTime < 0 && count > 0) FirstAudioTime = Time.realtimeSinceStartup;
        }

        void Write(short s)
        {
            if (written - consumed >= ring.Length) return; // overflow guard (should not happen: speech is ~realtime)
            ring[written % ring.Length] = s / 32768f; written++;
        }

        void OnRead(float[] data)
        {
            lock (gate)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (consumed < written) { data[i] = ring[consumed % ring.Length]; consumed++; playedAudible = consumed; lastDataOutPos = outPos + 1; if (firstDataOutPos < 0) firstDataOutPos = outPos; }
                    else data[i] = 0f; // waiting for more audio (or finished)
                    outPos++;
                }
            }
        }

        /// <summary>
        /// True when the stream has ended and the AudioSource playhead (timeSamples of this clip) has passed the clip
        /// position of the last real sample. Silence inserted while waiting for data is accounted for.
        /// </summary>
        public bool PlayedOut(int timeSamples)
        {
            lock (gate)
            {
                if (Cancelled) return true;
                if (!Completed || consumed < written) return false;
                return timeSamples >= lastDataOutPos || written == 0;
            }
        }
        /// <summary>
        /// Where playback must start: Unity pre-reads 0.8 s from a streamed clip when it is CREATED (before any speech has
        /// arrived), so the real audio begins after that silence. Starting the playhead here removes 0.8 s of dead air per line.
        /// </summary>
        public int StartClipPosition { get { lock (gate) return (int)(firstDataOutPos >= 0 ? firstDataOutPos : outPos); } }
        /// <summary>Samples handed to Unity so far (its read-ahead position in the clip).</summary>
        public long ReadPosition { get { lock (gate) return outPos; } }
        /// <summary>Clip position just after the last real sample handed to Unity (test/diagnostics).</summary>
        public long LastDataClipPosition { get { lock (gate) return lastDataOutPos; } }

        public void Complete() => Completed = true;
        public void Cancel() { Cancelled = true; lock (gate) { consumed = written; playedAudible = written; } }
    }

    /// <summary>Parses the proxy's framed response: one JSON line (validated text + classification), then raw PCM.</summary>
    public class VoiceStreamHandler : DownloadHandlerScript
    {
        readonly StreamingVoice voice; readonly Action<string> onMeta;
        readonly StringBuilder header = new StringBuilder(); bool metaDone; readonly System.Collections.Generic.List<byte> headerBytes = new System.Collections.Generic.List<byte>();
        public string Meta { get; private set; }

        public VoiceStreamHandler(StreamingVoice voice, Action<string> onMeta) : base(new byte[16384]) { this.voice = voice; this.onMeta = onMeta; }

        protected override bool ReceiveData(byte[] data, int len)
        {
            if (data == null || len <= 0) return true;
            int start = 0;
            if (!metaDone)
            {
                for (; start < len; start++)
                {
                    if (data[start] == (byte)'\n') { metaDone = true; Meta = Encoding.UTF8.GetString(headerBytes.ToArray()); start++; try { onMeta?.Invoke(Meta); } catch (Exception e) { Debug.LogException(e); } break; }
                    headerBytes.Add(data[start]);
                }
                if (!metaDone) return true;
            }
            if (start < len) voice.PushPcm16(data, start, len - start);
            return true;
        }

        protected override void CompleteContent() { voice.Complete(); }
    }
}
