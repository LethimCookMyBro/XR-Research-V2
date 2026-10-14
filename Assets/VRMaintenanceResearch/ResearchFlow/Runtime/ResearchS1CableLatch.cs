using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(9000)]
    public sealed class ResearchS1CableLatch : MonoBehaviour
    {
        public QAPlug plug;
        public ResearchS1TaskAInput input;
        public QALatch latch;
        public QASocket[] sockets;
        private void LateUpdate()
        {
            latch.socket = plug.Socket ? plug.Socket : sockets[0];
            if (plug.IsHeld && input.Allowed && latch.Closed)
            {
                var before = input.owner.Capture();
                bool connected = plug.Socket && plug.Socket.Retained;
                if (latch.Open() && connected) input.Motion("UNFASTEN", before, false);
            }
            else if (!plug.IsHeld && !plug.Socket && !latch.Closed) latch.Close();
        }
    }
}
