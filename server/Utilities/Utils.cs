using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    public sealed class AssertionFailedException : Exception
    {
        public AssertionFailedException() { }
        public AssertionFailedException(string message) : base(message) { }
    }

    public sealed class DebugLog : EventListener
    {
        public static DebugLog Logger
        {
            get
            {
                if (mLogger == null)
                    mLogger = new DebugLog();

                return mLogger;
            }
        }

        private static DebugLog mLogger;

        public static void Register(EventSource source)
        {
        }

        private string mFormat;

        private DebugLog()
        {
            var process = Process.GetCurrentProcess();
            mFormat = string.Format("[{0}] {{0:yyyy'-'MM'-'dd' 'HH':'mm':'ss}} {{1}}({{2}})", process.ProcessName);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            var message = String.Format(CultureInfo.InvariantCulture, mFormat, DateTime.UtcNow, e.EventName, String.Join(", ", e.Payload.Select(x => (x ?? "<null>").ToString())));
            Debug.WriteLine(message);
            ConsoleAccess.WriteLine(message);
        }
    }

    public static partial class Utils
    {
        public static void Bootstrap()
        {
            AppDomain.CurrentDomain.UnhandledException += ReportUnhandledException;

            var thread = Thread.CurrentThread;
            var culture = CultureInfo.InvariantCulture;
            thread.CurrentCulture = culture;
            thread.CurrentUICulture = culture;
        }

        public static void ReportException(object exception, bool terminate)
        {
            try
            {
                CoreEvents.Log.UnhandledException(exception);

                var sb = new StringBuilder();
                sb.Append(DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));
                sb.Append("  Unhandled Exception: ");
                sb.Append(exception);

                ConsoleAccess.WriteError(sb.ToString());
            }
            finally
            {
                if (terminate)
                {
                    ConsoleAccess.Flush();
                    Environment.Exit(0xBADF00D);
                }
            }
        }

        private static void ReportUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportException(e.ExceptionObject, true);
        }

        [DebuggerStepThrough]
        public static void Swap<T>(ref T a, ref T b)
        {
            T c = a; a = b; b = c;
        }

        [DebuggerStepThrough]
        public static void Assert(bool condition)
        {
            if (!condition)
                throw new AssertionFailedException();
        }

        [DebuggerStepThrough]
        public static SynchronizationContext RequireContext()
        {
            var context = SynchronizationContext.Current;
            if (context == null)
                throw new NotSupportedException("No SynchronizationContext available");

            return context;
        }
    }
}
