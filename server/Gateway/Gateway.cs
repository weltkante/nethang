using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    internal sealed class GatewayPort
    {
        private Acceptor mClientAcceptor;
        private GatewayControllerConnection mControlConnection;
        private GatewayControllerConnection mPendingControlConnection;
        private Dictionary<int, GatewayClientConnection> mClientConnections = new Dictionary<int, GatewayClientConnection>();
        private Random mClientIdGenerator = new Random();
        private int mPort;
        private bool mDisposed;

        public GatewayPort(int port)
        {
            GatewayEvents.Log.GatewayCreated(port);
            
            mPort = port;
            mClientAcceptor = new Acceptor(IPAddress.Any, port, AcceptClientConnection);
        }

        public void Dispose()
        {
            GatewayServer.RequireServerThread();

            if(!mDisposed)
            {
                mDisposed = true;

                GatewayEvents.Log.GatewayDisposed(mPort);

                mClientAcceptor.Dispose();

                if(mControlConnection != null)
                    mControlConnection.Dispose();

                if(mPendingControlConnection != null)
                    mPendingControlConnection.Dispose();

                foreach(var client in mClientConnections.Values)
                    client.Dispose();
            }
        }

        private void CheckDisposed()
        {
            if(mDisposed)
                throw new ObjectDisposedException(null);
        }

        public int Port
        {
            get { return mPort; }
        }

        public GatewayControllerConnection Controller
        {
            get
            {
                CheckDisposed();
                return mControlConnection;
            }
        }

        public ICollection<GatewayClientConnection> Clients
        {
            get
            {
                CheckDisposed();
                return mClientConnections.Values;
            }
        }

        public void Activate(GatewayControllerConnection controller)
        {
            CheckDisposed();

            if(mControlConnection == null)
            {
                mControlConnection = controller;
                controller.Activate(this);
            }
            else if(mPendingControlConnection == null)
            {
                mPendingControlConnection = controller;
                controller.ActivatePending();

                mControlConnection.CheckConnectionState();
            }
            else
            {
                controller.NotifyActivateRejected(this);
            }
        }

        public void NotifyConnectionAlive(GatewayControllerConnection controller)
        {
            CheckDisposed();

            Utils.Assert(mControlConnection == controller);
            Utils.Assert(mPendingControlConnection != null);
            mPendingControlConnection.NotifyActivateRejected(this);
            mPendingControlConnection = null;
        }

        public void NotifyConnectionDead(GatewayControllerConnection controller)
        {
            CheckDisposed();

            Utils.Assert(mControlConnection == controller);
            mControlConnection.Dispose();
            mControlConnection = null;

            if(mPendingControlConnection != null)
            {
                mControlConnection = mPendingControlConnection;
                mPendingControlConnection = null;
                mControlConnection.Activate(this);
            }
        }

        private void AcceptClientConnection(Socket socket)
        {
            CheckDisposed();
            int clientId;
            do { clientId = mClientIdGenerator.Next(); }
            while(clientId <= 0 || mClientConnections.ContainsKey(clientId));
            mClientConnections.Add(clientId, new GatewayClientConnection(this, socket, clientId));
        }

        public GatewayClientConnection ParseClientId(int clientId)
        {
            CheckDisposed();
            GatewayClientConnection client;
            ProtocolException.Assert(mClientConnections.TryGetValue(clientId, out client));
            return client;
        }

        public bool IsReady
        {
            get
            {
                CheckDisposed();
                return mControlConnection != null && mControlConnection.IsReady;
            }
        }

        internal void NotifyClientTerm(GatewayClientConnection client)
        {
            CheckDisposed();

            Utils.Assert(mClientConnections.Remove(client.Id));
        }

        internal void NotifyClientDead(GatewayClientConnection client)
        {
            CheckDisposed();

            if(IsReady)
                mControlConnection.NotifyClientDead(client);
            else
                Utils.Assert(mClientConnections.Remove(client.Id));
        }

        internal void KillClient(GatewayClientConnection client)
        {
            CheckDisposed();
            Utils.Assert(IsReady);
            Utils.Assert(mClientConnections.Remove(client.Id));
            client.Dispose();
        }
    }
}
