using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    public enum ClientIdMismatchType
    {
        InitDuplicate = 1,
        DataMissing = 2,
        TermMissing = 3,
        DeadMissing = 4,
    }

    [EventSource(Name = "myapp-server")]
    internal sealed class ServerEvents : EventSource
    {
        public static readonly ServerEvents Log = new ServerEvents();
        private ServerEvents() { DebugLog.Register(this); }

        [Event(1)]
        public void BeginStartup()
        {
            WriteEvent(1);
        }

        [Event(2)]
        public void StartupComplete()
        {
            WriteEvent(2);
        }

        [Event(3)]
        public void BeginShutdown()
        {
            WriteEvent(3);
        }

        [Event(4)]
        public void ShutdownComplete()
        {
            WriteEvent(4);
        }

        [Event(5, Level = EventLevel.Warning)]
        public void Warning(string message)
        {
            WriteEvent(5, message);
        }

        [Event(6, Level = EventLevel.Error)]
        public void Error(string message)
        {
            WriteEvent(6, message);
        }

        [NonEvent]
        public void ClientProtocolException(ClientConnection client, ProtocolException ex)
        {
            if (IsEnabled())
            {
                ClientProtocolException(client.GatewayClientId, client.RemoteEndpoint, null, null, ex.Message);
            }
        }

        [Event(10)]
        public void ClientProtocolException(int clientId, string endpoint, string account, string avatar, string exception)
        {
            WriteEvent(10, clientId, endpoint, account, avatar, exception);
        }

        [Event(11)]
        public void ClientConnect(int clientId, string endpoint)
        {
            WriteEvent(11, clientId, endpoint);
        }

        [Event(14)]
        public void GatewayDisconnect()
        {
            WriteEvent(14);
        }

        [Event(15)]
        public void GatewayConnectionCheck()
        {
            WriteEvent(15);
        }
    }
}
