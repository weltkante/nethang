using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    public sealed partial class ClientConnection
    {
        internal enum ServerState : byte { Handshake, Disposed }

        private GatewayConnector mGateway;
        private ClientProtocol mProtocol;
        private string mEndpoint;
        private int mClientId;
        private uint mClientSequenceId;
        private ServerState mServerState = ServerState.Handshake;

        internal ClientConnection(GatewayConnector gateway, int clientId, string endpoint)
        {
            mGateway = gateway;
            mClientId = clientId;
            mEndpoint = endpoint;
            mProtocol = new ClientProtocol(this);
        }

        internal void Initialize(ArraySegment<byte> data, bool complete)
        {
            ServerEvents.Log.ClientConnect(mClientId, mEndpoint);

            if (!IsDisposed)
                ProcessData(data, complete);
        }

        internal void ProcessData(ArraySegment<byte> data, bool complete)
        {
            Utils.Assert(!IsDisposed);

            try
            {
                mProtocol.ProcessData(data, complete);
            }
            catch (ProtocolException ex)
            {
                ServerEvents.Log.ClientProtocolException(this, ex);
                Dispose();
                return;
            }
        }

        private bool CheckSequenceId(uint value1, uint value0)
        {
            return (sbyte)(value1 - value0) >= 0;
        }

        internal void UpdateClientSequenceId(PassiveScope tx, uint sequenceId)
        {
            if (mClientSequenceId == sequenceId)
                return;

            if (!CheckSequenceId(sequenceId, mClientSequenceId))
                throw new ProtocolException();

            var oldSequenceId = mClientSequenceId;
            tx.Context.Record(() => mClientSequenceId = oldSequenceId);
            mClientSequenceId = sequenceId;
        }

        internal void SendFlushedPages(ClientOutputBuffer buffer, int processed, bool shutdown)
        {
            buffer.SendFlushedPages(this, mGateway, processed, shutdown);

            if (shutdown)
                Dispose();
        }

        public void Dispose()
        {
            if (mServerState != ServerState.Disposed)
            {
                mServerState = ServerState.Disposed;
                mGateway.NotifyDispose(this);
            }
        }

        public bool IsDisposed
        {
            get { return mServerState == ServerState.Disposed; }
        }

        public int GatewayClientId
        {
            get { return mClientId; }
        }

        public string RemoteEndpoint
        {
            get { return mEndpoint; }
        }
    }
}
