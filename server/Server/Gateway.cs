using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    internal sealed class GatewayConnector : GatewayProtocol.IGatewayClient
    {
        private GatewayProtocolWrapper.Client mProtocol;
        private bool mDisposed;
        private Dictionary<int, ClientConnection> mClients;

        internal GatewayConnector()
            : this(new IPEndPoint(IPAddress.Loopback, GatewayProtocol.kPort))
        {
        }

        internal GatewayConnector(IPEndPoint endpoint)
        {
            mClients = new Dictionary<int, ClientConnection>();
            mProtocol = new GatewayProtocolWrapper.Client(this);
            SocketUplink.Connect(endpoint).SetProtocol(mProtocol, true);
            using (var p = mProtocol.BeginPacket())
                GatewayProtocol.SendDoActivate(p, Config.Server.ClientPort);
        }

        internal void DisposeInternal()
        {
            if (!mDisposed)
            {
                mDisposed = true;
                mProtocol.Dispose();

                foreach (var client in mClients.Values.ToArray())
                    client.Dispose();

                Utils.Assert(mClients.Count == 0);
            }
        }

        private void InitiateShutdown()
        {
            DisposeInternal();
            Program.InitiateShutdown();
        }

        internal void NotifyReady()
        {
            using (var p = mProtocol.BeginPacket())
                GatewayProtocol.SendDoReady(p, Config.Server.ClientPort);
        }

        internal void NotifyDispose(ClientConnection client)
        {
            Utils.Assert(client.IsDisposed);
            if (mClients.Remove(client.GatewayClientId) && !mDisposed)
                using (var p = mProtocol.BeginPacket())
                    GatewayProtocol.SendDoKill(p, client.GatewayClientId);
        }

        internal void SendClientData(ClientConnection client, IList<ArraySegment<byte>> data, int processed, bool shutdown)
        {
            Utils.Assert(!client.IsDisposed);
            using (var p = mProtocol.BeginPacket())
            {
                GatewayProtocol.SendDoSetState(p, client.GatewayClientId, Array.Empty<byte>());

                if (data.Count > 0)
                {
                    p.WriteByte(GatewayProtocol.kDoSendData);
                    p.WriteInt32(client.GatewayClientId);
                    p.WritePackedUInt32(checked((uint)data.Sum(x => x.Count)));
                    foreach (var part in data)
                        p.WriteBytes(part.Array, part.Offset, part.Count);
                }

                if (processed > 0)
                    GatewayProtocol.SendDoProcess(p, client.GatewayClientId, processed);

                if (shutdown)
                {
                    Utils.Assert(mClients.Remove(client.GatewayClientId));
                    GatewayProtocol.SendDoTerm(p, client.GatewayClientId);
                }

                GatewayProtocol.SendDoCommit(p, client.GatewayClientId);
            }
        }

        void GatewayProtocol.IGatewayClient.Disconnect(bool hard)
        {
            ServerEvents.Log.GatewayDisconnect();
            InitiateShutdown();
        }

        void GatewayProtocol.IGatewayClient.CheckConnection()
        {
            ServerEvents.Log.GatewayConnectionCheck();
            using (var p = mProtocol.BeginPacket())
                GatewayProtocol.SendAckConnectionCheck(p);
        }

        void GatewayProtocol.IGatewayClient.CheckConnection2()
        {
            Program.NotifyGatewayIsAlive();
        }

        void GatewayProtocol.IGatewayClient.OnActivate(int port, bool success)
        {
            if (success)
                Program.GatewayActivated();
            else
                InitiateShutdown();
        }

        void GatewayProtocol.IGatewayClient.OnDeactivate(int port)
        {
            InitiateShutdown();
        }

        void GatewayProtocol.IGatewayClient.OnClientInit(int clientId, bool complete, string endpoint, ArraySegment<byte> state, ArraySegment<byte> data)
        {
            ClientConnection client;
            if (mClients.TryGetValue(clientId, out client))
            {
                mClients.Remove(clientId);
                client.Dispose();
            }

            client = new ClientConnection(this, clientId, endpoint);
            mClients.Add(clientId, client);
            client.Initialize(data, complete);
        }

        void GatewayProtocol.IGatewayClient.OnClientData(int clientId, ArraySegment<byte> data)
        {
            ClientConnection client;
            if (mClients.TryGetValue(clientId, out client))
                client.ProcessData(data, false);
        }

        void GatewayProtocol.IGatewayClient.OnClientTerm(int clientId)
        {
            ClientConnection client;
            if (mClients.TryGetValue(clientId, out client))
                client.ProcessData(default(ArraySegment<byte>), true);
        }

        void GatewayProtocol.IGatewayClient.OnClientDead(int clientId)
        {
            ClientConnection client;
            if (mClients.TryGetValue(clientId, out client))
                client.Dispose();
        }
    }
}
