using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    internal sealed class GatewayManager: IDisposable
    {
        private Acceptor mControlAcceptor;
        private HashSet<GatewayControllerConnection> mControlConnections = new HashSet<GatewayControllerConnection>();
        private Dictionary<int, GatewayPort> mPorts = new Dictionary<int, GatewayPort>();

        public GatewayManager()
        {
            mControlAcceptor = new Acceptor(IPAddress.Loopback, GatewayProtocol.kPort, AcceptControlConnection);
        }

        public void Dispose()
        {
            GatewayServer.RequireServerThread();

            mControlAcceptor.Dispose();

            foreach(var connection in mControlConnections)
                connection.Dispose();

            foreach(var port in mPorts.Values)
                port.Dispose();
        }

        private void AcceptControlConnection(Socket socket)
        {
            mControlConnections.Add(new GatewayControllerConnection(this, socket));
        }

        public void ActivatePort(int port, GatewayControllerConnection controller)
        {
            GatewayPort gateway;
            if(!mPorts.TryGetValue(port, out gateway))
                mPorts.Add(port, gateway = new GatewayPort(port));

            gateway.Activate(controller);
        }
    }
}
