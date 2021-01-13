using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    [EventSource(Name = "myapp-gateway")]
    public sealed class GatewayEvents: EventSource
    {
        public static readonly GatewayEvents Log = new GatewayEvents();
        private GatewayEvents() { DebugLog.Register(this); }

        public static class Tasks
        {
            public const EventTask Gateway = (EventTask)1;
            public const EventTask Controller = (EventTask)2;
            public const EventTask Client = (EventTask)3;
        }

        public static class Opcodes
        {
            public const EventOpcode Disconnect = (EventOpcode)11;
            public const EventOpcode Activated = (EventOpcode)12;
            public const EventOpcode Rejected = (EventOpcode)13;
            public const EventOpcode DoCommit = (EventOpcode)14;
            public const EventOpcode DoSetState = (EventOpcode)15;
            public const EventOpcode DoProcess = (EventOpcode)16;
            public const EventOpcode DoSend = (EventOpcode)17;
            public const EventOpcode DoTerm = (EventOpcode)18;
            public const EventOpcode DoKill = (EventOpcode)19;
            public const EventOpcode DoReady = (EventOpcode)20;
            public const EventOpcode DoActivate = (EventOpcode)21;
            public const EventOpcode DoDeactivate = (EventOpcode)22;
        }

        [Event(1, Level = EventLevel.Informational, Task = Tasks.Controller, Opcode = EventOpcode.Start)]
        public void ControllerCreated(int ControllerId)
        {
            WriteEvent(1, ControllerId);
        }

        [Event(2, Level = EventLevel.Informational, Task = Tasks.Controller, Opcode = EventOpcode.Stop)]
        public void ControllerDisposed(int ControllerId)
        {
            WriteEvent(2, ControllerId);
        }

        [Event(26, Level = EventLevel.Informational, Task = Tasks.Controller, Opcode = Opcodes.Disconnect)]
        public void ControllerDisconnect(int ControllerId)
        {
            WriteEvent(26, ControllerId);
        }

        [Event(3, Level = EventLevel.Informational, Task = Tasks.Controller, Opcode = Opcodes.Activated)]
        public void ControllerActivated(int ControllerId, int port)
        {
            WriteEvent(3, ControllerId, port);
        }

        [Event(4, Level = EventLevel.Informational, Task = Tasks.Controller, Opcode = Opcodes.Rejected)]
        public void ControllerRejected(int ControllerId, int port)
        {
            WriteEvent(4, ControllerId, port);
        }

        [Event(5, Level = EventLevel.Informational, Task = Tasks.Controller, Opcode = Opcodes.DoCommit)]
        public void ControllerDoCommit(int ControllerId, int clientId)
        {
            WriteEvent(5, ControllerId, clientId);
        }

        [Event(6, Level = EventLevel.Verbose, Task = Tasks.Controller, Opcode = Opcodes.DoSetState)]
        public void ControllerDoSetState(int ControllerId, int clientId, int length)
        {
            WriteEvent(6, ControllerId, clientId, length);
        }

        [Event(7, Level = EventLevel.Verbose, Task = Tasks.Controller, Opcode = Opcodes.DoProcess)]
        public void ControllerDoProcess(int ControllerId, int clientId, int length)
        {
            WriteEvent(7, ControllerId, clientId, length);
        }

        [Event(8, Level = EventLevel.Verbose, Task = Tasks.Controller, Opcode = Opcodes.DoSend)]
        public void ControllerDoSend(int ControllerId, int clientId, int length)
        {
            WriteEvent(8, ControllerId, clientId, length);
        }

        [Event(9, Level = EventLevel.Verbose, Task = Tasks.Controller, Opcode = Opcodes.DoTerm)]
        public void ControllerDoTerm(int ControllerId, int clientId)
        {
            WriteEvent(9, ControllerId, clientId);
        }

        [Event(10, Level = EventLevel.Verbose, Task = Tasks.Controller, Opcode = Opcodes.DoKill)]
        public void ControllerDoKill(int ControllerId, int clientId)
        {
            WriteEvent(10, ControllerId, clientId);
        }

        [Event(11, Level = EventLevel.Verbose, Task = Tasks.Controller, Opcode = Opcodes.DoReady)]
        public void ControllerDoReady(int ControllerId, int port)
        {
            WriteEvent(11, ControllerId, port);
        }

        [Event(12, Level = EventLevel.Verbose, Task = Tasks.Controller, Opcode = Opcodes.DoActivate)]
        public void ControllerDoActivate(int ControllerId, int port)
        {
            WriteEvent(12, ControllerId, port);
        }

        [Event(13, Level = EventLevel.Verbose, Task = Tasks.Controller, Opcode = Opcodes.DoDeactivate)]
        public void ControllerDoDeactivate(int ControllerId, int port)
        {
            WriteEvent(13, ControllerId, port);
        }

        [NonEvent]
        internal void ClientConnected(GatewayClientConnection client)
        {
            ClientConnected(client.Id, client.Gateway.Port, client.RemoteEndpoint.ToString());
        }

        [Event(20, Level = EventLevel.Informational, Task = Tasks.Client, Opcode = EventOpcode.Start)]
        public void ClientConnected(int clientId, int port, string endpoint)
        {
            WriteEvent(20, endpoint, port, clientId);
        }

        [Event(21, Level = EventLevel.Informational, Task = Tasks.Client, Opcode = EventOpcode.Stop)]
        public void ClientDisposed(int clientId)
        {
            WriteEvent(21, clientId);
        }

        [Event(22, Level = EventLevel.Verbose)]
        public void ClientReceivedData(int clientId, int length)
        {
            WriteEvent(22, clientId, length);
        }

        [Event(23, Level = EventLevel.Verbose)]
        public void ClientRemoteDisconnected(int clientId)
        {
            WriteEvent(23, clientId);
        }

        [Event(24, Level = EventLevel.Verbose)]
        public void ClientSentData(int clientId, int length)
        {
            WriteEvent(24, clientId, length);
        }

        [Event(25, Level = EventLevel.Verbose)]
        public void ClientGracefulDisconnect(int clientId)
        {
            WriteEvent(25, clientId);
        }

        [Event(40, Level = EventLevel.Informational, Task = Tasks.Gateway, Opcode = EventOpcode.Start)]
        public void GatewayCreated(int Port)
        {
            WriteEvent(40, Port);
        }

        [Event(41, Level = EventLevel.Informational, Task = Tasks.Gateway, Opcode = EventOpcode.Stop)]
        public void GatewayDisposed(int Port)
        {
            WriteEvent(41, Port);
        }

        [Event(42, Level = EventLevel.Informational, Task = Tasks.Gateway)]
        public void GatewayControllerDropped(int Port, string Error)
        {
            WriteEvent(42, Error, Port);
        }
    }
}
