using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    public sealed class GatewayServer : IDisposable
    {
        private static ServerThread mServer;
        private static GatewayManager mGateway;
        private static int mShutdownInitiated;

        public static void RequireServerThread() => Utils.Assert(mServer.IsCurrent);

        public GatewayServer()
        {
            lock (GetType())
            {
                if (mServer != null)
                    throw new NotSupportedException();

                mServer = new ServerThread();
                mServer.Context.Send(x => mGateway = new GatewayManager(), null);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref mShutdownInitiated, 1) == 0)
            {
                mServer.Context.Post(x => mGateway.Dispose(), null);
                mServer.DisposeAndJoin();
            }
            else
            {
                mServer.Join();
            }
        }
    }
}
