using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    internal sealed class GatewayControllerConnection: IDisposable, GatewayProtocol.IGatewayServer
    {
        private enum ConnectionState
        {
            Initializing,
            Unbound,
            Activating,
            PendingActivationCheck,
            Deactivated,
            Activated,
            Online,
            Disposed,
        }

        private static int mNextLogId = 0;

        private GatewayManager mManager;
        private GatewayPort mGateway;
        private GatewayProtocolWrapper mProtocol;
        private ConnectionState mState;
        private int mLogId = Interlocked.Increment(ref mNextLogId);
        private bool mIsReady;
        private bool mConnectionCheck;
        private Timer mTimer;

        public GatewayControllerConnection(GatewayManager manager, Socket socket)
        {
            GatewayEvents.Log.ControllerCreated(mLogId);

            mState = ConnectionState.Initializing;
            mManager = manager;
            mTimer = new Timer(KeepAliveTimerCallback, SynchronizationContext.Current, Timeout.Infinite, Timeout.Infinite);
            mProtocol = new GatewayProtocolWrapper.Server(this);

            mState = ConnectionState.Unbound;
            new SocketUplink(socket).SetProtocol(mProtocol, true);
            mTimer.Change(500, 500);
        }

        private void KeepAliveTimerCallback(object state)
        {
            var sc = (SynchronizationContext)state;
            sc.Post(x =>
            {
                if (mIsReady && mState == ConnectionState.Online)
                    using (var p = mProtocol.BeginPacket())
                        GatewayProtocol.SendCheckConnection2(p);

            }, null);
        }

        public void Dispose()
        {
            if(mState != ConnectionState.Disposed)
            {
                mState = ConnectionState.Disposed;
                mTimer.Dispose();
                mProtocol.Dispose();
                GatewayEvents.Log.ControllerDisposed(mLogId);
            }
        }

        public bool IsReady
        {
            get { return mIsReady; }
        }

        void GatewayProtocol.IGatewayServer.Disconnect(bool hard)
        {
            GatewayEvents.Log.ControllerDisconnect(mLogId);
            Dispose();
            mGateway.NotifyConnectionDead(this);
        }

        void GatewayProtocol.IGatewayServer.AckConnectionCheck()
        {
            ProtocolException.Assert(mConnectionCheck);
            mConnectionCheck = false;
            mGateway.NotifyConnectionAlive(this);
        }

        void GatewayProtocol.IGatewayServer.DoActivate(int port)
        {
            GatewayEvents.Log.ControllerDoActivate(mLogId, port);
            ProtocolException.Assert(mState == ConnectionState.Unbound);
            Utils.Assert(mGateway == null);

            mState = ConnectionState.Activating;
            mManager.ActivatePort(port, this);
            Utils.Assert(mState == ConnectionState.Activated || mState == ConnectionState.PendingActivationCheck || mState == ConnectionState.Unbound);
        }

        void GatewayProtocol.IGatewayServer.DoReady(int port)
        {
            GatewayEvents.Log.ControllerDoReady(mLogId, port);
            ProtocolException.Assert(mState == ConnectionState.Activated);
            Utils.Assert(mGateway != null);
            ProtocolException.Assert(mGateway.Port == port);
            ProtocolException.Assert(!mIsReady);

            mIsReady = true;
            mState = ConnectionState.Online;
            NotifyReady(mGateway);
        }

        void GatewayProtocol.IGatewayServer.DoDeactivate(int port)
        {
            GatewayEvents.Log.ControllerDoDeactivate(mLogId, port);
            ProtocolException.Assert(mState == ConnectionState.Activated || mState == ConnectionState.Online);
            Utils.Assert(mGateway != null);
            ProtocolException.Assert(mGateway.Port == port);
            throw new NotImplementedException();
        }

        void GatewayProtocol.IGatewayServer.DoSetState(int clientId, byte[] data)
        {
            GatewayEvents.Log.ControllerDoSetState(mLogId, clientId, data.Length);
            ProtocolException.Assert(mState == ConnectionState.Online);
            Utils.Assert(mGateway != null);
            Utils.Assert(mIsReady);
            mGateway.ParseClientId(clientId).TxSetState(data);
        }

        void GatewayProtocol.IGatewayServer.DoProcess(int clientId, int length)
        {
            GatewayEvents.Log.ControllerDoProcess(mLogId, clientId, length);
            ProtocolException.Assert(mState == ConnectionState.Online);
            Utils.Assert(mGateway != null);
            Utils.Assert(mIsReady);
            mGateway.ParseClientId(clientId).TxProcess(length);
        }

        void GatewayProtocol.IGatewayServer.DoSendData(int clientId, ArraySegment<byte> data)
        {
            GatewayEvents.Log.ControllerDoSend(mLogId, clientId, data.Count);
            ProtocolException.Assert(mState == ConnectionState.Online);
            Utils.Assert(mGateway != null);
            Utils.Assert(mIsReady);
            mGateway.ParseClientId(clientId).TxSend(data);
        }

        void GatewayProtocol.IGatewayServer.DoTerm(int clientId)
        {
            GatewayEvents.Log.ControllerDoTerm(mLogId, clientId);
            ProtocolException.Assert(mState == ConnectionState.Online);
            Utils.Assert(mGateway != null);
            Utils.Assert(mIsReady);
            mGateway.ParseClientId(clientId).TxDisconnect();
        }

        void GatewayProtocol.IGatewayServer.DoKill(int clientId)
        {
            GatewayEvents.Log.ControllerDoKill(mLogId, clientId);
            ProtocolException.Assert(mState == ConnectionState.Online);
            Utils.Assert(mGateway != null);
            Utils.Assert(mIsReady);
            mGateway.KillClient(mGateway.ParseClientId(clientId));
        }

        void GatewayProtocol.IGatewayServer.DoCommit(int clientId)
        {
            GatewayEvents.Log.ControllerDoCommit(mLogId, clientId);
            ProtocolException.Assert(mState == ConnectionState.Online);
            Utils.Assert(mGateway != null);
            Utils.Assert(mIsReady);
            mGateway.ParseClientId(clientId).Commit();
        }

        public void Activate(GatewayPort gateway)
        {
            Utils.Assert(mState == ConnectionState.Activating || mState == ConnectionState.PendingActivationCheck);
            Utils.Assert(mGateway == null);

            mGateway = gateway;
            mState = ConnectionState.Activated;

            using(var p = mProtocol.BeginPacket())
                GatewayProtocol.SendOnActivate(p, mGateway.Port, true);
        }

        public void ActivatePending()
        {
            Utils.Assert(mState == ConnectionState.Activating);

            mState = ConnectionState.PendingActivationCheck;
        }

        public void CheckConnectionState()
        {
            Utils.Assert(!mConnectionCheck);
            mConnectionCheck = true;
            using(var p = mProtocol.BeginPacket())
                GatewayProtocol.SendCheckConnection(p);
        }

        public void NotifyActivateRejected(GatewayPort gateway)
        {
            Utils.Assert(mState == ConnectionState.Activating || mState == ConnectionState.PendingActivationCheck);
            Utils.Assert(gateway == mGateway);

            using(var p = mProtocol.BeginPacket())
                GatewayProtocol.SendOnActivate(p, gateway.Port, false);

            mState = ConnectionState.Unbound;
        }

        public void NotifyReady(GatewayPort gateway)
        {
            Utils.Assert(gateway == mGateway);

            if(gateway.Clients.Count > 0)
            {
                using(var p = mProtocol.BeginPacket())
                {
                    foreach(var client in gateway.Clients)
                    {
                        client.Rollback();
                        client.WriteInitPacket(p);
                    }
                }
            }
        }

        internal void NotifyClientConnect(GatewayClientConnection client)
        {
            Utils.Assert(mIsReady);

            using(var p = mProtocol.BeginPacket())
                client.WriteInitPacket(p);
        }

        internal void NotifyClientData(GatewayClientConnection client, ArraySegment<byte> data)
        {
            Utils.Assert(mIsReady);

            using(var p = mProtocol.BeginPacket())
                GatewayProtocol.SendOnClientData(p, client.Id, data);
        }

        internal void NotifyClientRemoteShutdown(GatewayClientConnection client)
        {
            Utils.Assert(mIsReady);

            using(var p = mProtocol.BeginPacket())
                GatewayProtocol.SendOnClientTerm(p, client.Id);
        }

        internal void NotifyClientDead(GatewayClientConnection client)
        {
            Utils.Assert(mIsReady);

            using(var p = mProtocol.BeginPacket())
                GatewayProtocol.SendOnClientDead(p, client.Id);
        }
    }
}
