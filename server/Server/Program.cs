using System;
using System.Diagnostics.Tracing;
using System.Threading;

namespace AppNamespace
{
    public static class Config
    {
        public static class Server
        {
            public static int ClientPort = 3720;
        }
    }

    public static class Program
    {
        private static ServerThread mServer;
        private static GatewayConnector mGateway;
        private static ManualResetEventSlim mStartupComplete;
        private static ManualResetEventSlim mShutdownComplete;
        private static Timer mWatchdogTimer;
        private static int mShutdown;
        private static int mWatchdogDebugger;
        private static int mGatewayTimestamp;
        private static int mGatewayOfflineReported;

        private static void Main(string[] args)
        {
            Utils.Bootstrap();
            DebugLog.Logger.EnableEvents(CoreEvents.Log, EventLevel.Informational, CoreEvents.Keywords.Network | CoreEvents.Keywords.Important);
            DebugLog.Logger.EnableEvents(GatewayEvents.Log, EventLevel.Warning, EventKeywords.All);
            DebugLog.Logger.EnableEvents(ServerEvents.Log, EventLevel.Informational, EventKeywords.All);
            GatewayServer gateway = new GatewayServer();
            ServerEvents.Log.BeginStartup();
            mStartupComplete = new ManualResetEventSlim();
            mShutdownComplete = new ManualResetEventSlim();
            mServer = new ServerThread();
            mServer.Context.Post(StartupStage1, null);
            mStartupComplete.Wait();
            if (!ShutdownInProgress)
            {
                ServerEvents.Log.StartupComplete();
                mGatewayTimestamp = Environment.TickCount;
                mWatchdogTimer = new Timer(WatchdogTimerCallback, null, 0, 1000);
            }
            mShutdownComplete.Wait();
            mServer.DisposeAndJoin();
            ServerEvents.Log.ShutdownComplete();
            gateway.Dispose();
        }

        private static void StartupStage1(object args)
        {
            mGateway = new GatewayConnector();
        }

        private static void StartupStage2()
        {
            ContextManager.Initialize();
            mGateway.NotifyReady();
            mStartupComplete.Set();
        }

        private static void ShutdownSequence(object unused)
        {
            mGateway.DisposeInternal();
            mShutdownComplete.Set();
            mStartupComplete.Set();
        }

        internal static void GatewayActivated()
        {
            mServer.Context.Post(x => StartupStage2(), null);
        }

        internal static void NotifyGatewayIsAlive()
        {
            Volatile.Write(ref mGatewayTimestamp, Environment.TickCount);
        }

        private static void WatchdogTimerCallback(object unused)
        {
            try
            {
                if (ShutdownInProgress)
                    return;

                var gatewayDelay = Environment.TickCount - Volatile.Read(ref mGatewayTimestamp);
                var gatewayIsOffline = (gatewayDelay > 2000);

                if (gatewayIsOffline)
                {
                    if (System.Diagnostics.Debugger.IsAttached)
                    {
                        ServerEvents.Log.Warning("Watchdog triggered while debugging");

                        if (gatewayDelay > 60000)
                        {
                            if (Interlocked.CompareExchange(ref mWatchdogDebugger, 1, 0) == 0)
                            {
                                System.Diagnostics.Debugger.Break();
                                Utils.Assert(Interlocked.Exchange(ref mWatchdogDebugger, 0) == 1);
                            }

                            Volatile.Write(ref mGatewayTimestamp, Environment.TickCount);
                        }
                    }
                    else
                    {
                        if (Interlocked.Exchange(ref mGatewayOfflineReported, 1) == 0)
                        {
                            ServerEvents.Log.Error("Gateway did not send regular pings, preparing for emergency shutdown.");
                            Utils.ReportException("Aborting process because gateway did not send regular pings.", false);
                        }

                        Volatile.Write(ref mGatewayTimestamp, Environment.TickCount);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.ReportException(ex, true);
            }
        }

        public static bool ShutdownInProgress
        {
            get { return Volatile.Read(ref mShutdown) != 0; }
        }

        public static void InitiateShutdown()
        {
            if (Interlocked.Exchange(ref mShutdown, 1) == 0)
            {
                ServerEvents.Log.BeginShutdown();
                mWatchdogTimer.Dispose();
                mServer.Context.Post(ShutdownSequence, null);
            }
        }
    }
}
