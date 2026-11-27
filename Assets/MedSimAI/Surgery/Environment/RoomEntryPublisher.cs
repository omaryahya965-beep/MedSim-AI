using System.Runtime.CompilerServices;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

[assembly: InternalsVisibleTo("MedSimAI.Surgery.Environment.Tests")]

namespace MedSimAI.Surgery.Environment
{
    [DisallowMultipleComponent]
    public sealed class RoomEntryPublisher : MonoBehaviour
    {
        [SerializeField] private Transform playerRoot;
        private SurgeryEventBus publishedBus;
        private string publishedSession;

        public void BindPlayer(Transform root) => playerRoot = root;

        private void OnTriggerEnter(Collider other) => ObservePlayer(other, SurgeryEventBusHost.Instance?.Bus);
        private void OnTriggerStay(Collider other) => ObservePlayer(other, SurgeryEventBusHost.Instance?.Bus);

        internal void ObservePlayer(Collider other, SurgeryEventBus bus)
        {
            if (!playerRoot || !other.transform.IsChildOf(playerRoot) || bus == null ||
                string.IsNullOrEmpty(bus.SessionId) || (bus == publishedBus && bus.SessionId == publishedSession))
                return;

            publishedBus = bus;
            publishedSession = bus.SessionId;
            bus.Publish(new SurgeryEvent(SurgeryEventIds.PlayerEnteredRoom, SurgeryEventSources.Environment));
        }
    }
}
