using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    public static class ConsoleAccess
    {
        private static TextWriter OutputWriter;
        private static TextWriter ErrorWriter;

        static ConsoleAccess()
        {
            OutputWriter = Console.Out;
            ErrorWriter = Console.Error;
            Flush();
        }

        public static void Flush()
        {
            OutputWriter.Flush();
            ErrorWriter.Flush();
        }

        public static void WriteError(string message)
        {
            ErrorWriter.WriteLine(message);
            ErrorWriter.Flush();
        }

        public static void WriteLine(string message)
        {
            Console.WriteLine(message);
        }
    }

    [EventSource(Name = "myapp-core")]
    public sealed class CoreEvents : EventSource
    {
        public static readonly CoreEvents Log = new CoreEvents();
        private CoreEvents() { DebugLog.Register(this); }

        public static class Keywords
        {
            public const EventKeywords Important = (EventKeywords)(1 << 0);
            public const EventKeywords Threads = (EventKeywords)(1 << 1);
            public const EventKeywords Network = (EventKeywords)(1 << 2);
        }

        [Event(1, Level = EventLevel.Critical)]
        public void UnhandledException(string ex)
        {
            WriteEvent(1, ex);
        }

        [NonEvent]
        public void UnhandledException(object ex)
        {
            UnhandledException(ex != null ? ex.ToString() : "<null>");
        }

        [Event(2, Level = EventLevel.Informational, Keywords = Keywords.Threads)]
        public void ServerThreadCreated()
        {
            WriteEvent(2);
        }

        [Event(3, Level = EventLevel.Informational, Keywords = Keywords.Threads)]
        public void ServerThreadBeginShutdown()
        {
            WriteEvent(3);
        }

        [Event(4, Level = EventLevel.Informational, Keywords = Keywords.Threads)]
        public void ServerThreadShutdownComplete()
        {
            WriteEvent(4);
        }

        [Event(5, Level = EventLevel.Verbose, Keywords = Keywords.Threads)]
        public void ServerThreadWaitingForTasks()
        {
            WriteEvent(5);
        }

        [Event(6, Level = EventLevel.Verbose, Keywords = Keywords.Threads)]
        public void ServerThreadFetchedTasks(int count)
        {
            WriteEvent(6, count);
        }

        [Event(7, Level = EventLevel.Error, Keywords = Keywords.Threads | Keywords.Important)]
        public void ServerThreadExceptionHolder(string error)
        {
            WriteEvent(7, error);
        }

        [Event(8, Level = EventLevel.Informational, Keywords = Keywords.Network)]
        public void AcceptorStarted(int port)
        {
            WriteEvent(8, port);
        }

        [Event(9, Level = EventLevel.Informational, Keywords = Keywords.Network)]
        public void AcceptorStopped(int port)
        {
            WriteEvent(9, port);
        }

        [Event(10, Level = EventLevel.Error, Keywords = Keywords.Network | Keywords.Important)]
        public void AcceptorError(int port, SocketError error)
        {
            WriteEvent(10, port, (int)error);
        }

        [Event(11, Level = EventLevel.Verbose, Keywords = Keywords.Network)]
        public void AcceptorAccept(int port, string endpoint)
        {
            WriteEvent(11, port, endpoint);
        }

        [NonEvent]
        public void AcceptorAccept(int port, Socket socket)
        {
            if (IsEnabled(EventLevel.Verbose, Keywords.Network))
                AcceptorAccept(port, socket.RemoteEndPoint.ToString());
        }

        [NonEvent]
        public void UplinkProtocolException(int id, ProtocolException ex)
        {
            UplinkException(id, ex.ToString());
        }

        [Event(12, Level = EventLevel.Error, Keywords = Keywords.Network | Keywords.Important)]
        public void UplinkException(int id, string error)
        {
            WriteEvent(12, id, error);
        }

        [Event(13, Level = EventLevel.Error, Keywords = Keywords.Network | Keywords.Important)]
        public void UplinkError(int id, SocketError error)
        {
            WriteEvent(13, id, (int)error);
        }

        [Event(14, Level = EventLevel.Informational, Keywords = Keywords.Network)]
        public void UplinkConnected(int id, string endpoint)
        {
            WriteEvent(14, id, endpoint);
        }

        [NonEvent]
        public void UplinkConnected(UplinkBase uplink)
        {
            UplinkConnected(uplink.LogId, uplink.ToString());
        }

        [Event(15, Level = EventLevel.Informational, Keywords = Keywords.Network)]
        public void UplinkRemoteShutdown(int id)
        {
            WriteEvent(15, id);
        }

        [Event(17, Level = EventLevel.Verbose, Keywords = Keywords.Network)]
        public void UplinkReceivedData(int id, int size)
        {
            WriteEvent(17, id, size);
        }
    }
}
