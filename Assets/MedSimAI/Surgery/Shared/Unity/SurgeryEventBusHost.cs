using UnityEngine;

namespace MedSimAI.Surgery.Shared.Unity
{
    /// <summary>
    /// Scene host for the session's SurgeryEventBus (one per surgical scene, placed by the integration lead; each test
    /// scene adds its own). Mechanics publish with SurgeryEventBusHost.Publish(...). The bus clock is Unity time.
    /// Instance is resolved lazily, so a host added from code (EditMode tests, builders) works before Awake runs.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public class SurgeryEventBusHost : MonoBehaviour
    {
        static SurgeryEventBusHost instance;
        public static SurgeryEventBusHost Instance
        {
            get
            {
                if (!instance) { instance = Object.FindFirstObjectByType<SurgeryEventBusHost>(); if (instance) instance.Init(); }
                return instance;
            }
        }
        public bool logToConsole = true;
        public SurgeryEventBus Bus { get; } = new SurgeryEventBus();
        bool initialized;

        void Init()
        {
            if (initialized) return;
            initialized = true;
            Bus.Clock = () => Time.timeAsDouble;
            Bus.OnSubscriberError = msg => Debug.LogWarning("[Surgery] event subscriber error: " + msg);
            Bus.EventPublished += e => { if (logToConsole) Debug.Log("[Surgery] " + e); };
        }

        void Awake()
        {
            if (instance && instance != this) { Destroy(this); return; }
            instance = this;
            Init();
        }
        void OnDestroy() { if (instance == this) instance = null; }

        public static void Publish(SurgeryEvent e)
        {
            var host = Instance;
            if (host) host.Bus.Publish(e);
            else Debug.LogWarning("[Surgery] No SurgeryEventBusHost in scene; event dropped: " + e?.id);
        }
    }
}
