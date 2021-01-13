using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    internal sealed class ServerThreadTask
    {
        private readonly SendOrPostCallback mCallback;
        private readonly object mState;
        private readonly ManualResetEventSlim mEvent;
        private ExceptionDispatchInfo mException;

        public ServerThreadTask(SendOrPostCallback callback, object state, bool sync)
        {
            this.mCallback = callback;
            this.mState = state;
            this.mEvent = sync ? new ManualResetEventSlim() : null;
        }

        public void Execute()
        {
            try
            {
                mCallback(mState);
            }
            catch (Exception ex)
            {
                if (mEvent != null)
                    mException = ExceptionDispatchInfo.Capture(ex);
                else
                    Utils.ReportException(ex, true);
            }

            if (mEvent != null)
                mEvent.Set();
        }

        public void Wait()
        {
            mEvent.Wait();

            if (mException != null)
                mException.Throw();
        }
    }

    internal sealed class ServerContext : SynchronizationContext
    {
        private readonly ServerThread mThread;

        public ServerContext(ServerThread thread)
        {
            mThread = thread;
        }

        public override SynchronizationContext CreateCopy()
        {
            return new ServerContext(mThread);
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            if (d == null)
                throw new ArgumentNullException(nameof(d));

            if (mThread.IsCurrent)
            {
                d(state);
            }
            else
            {
                var task = new ServerThreadTask(d, state, true);
                mThread.PostServerTask(task);
                task.Wait();
            }
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            if (d == null)
                throw new ArgumentNullException(nameof(d));

            mThread.PostServerTask(new ServerThreadTask(d, state, false));
        }

        public override void OperationStarted()
        {
            mThread.IncrementUsageCount();
        }

        public override void OperationCompleted()
        {
            mThread.DecrementUsageCount();
        }
    }

    public sealed class ServerThread : IDisposable
    {
        private enum State { Running, Shutdown, Terminated }

        private readonly Thread mThread;
        private readonly CultureInfo mCurrentCulture;
        private readonly CultureInfo mCurrentUICulture;
        private readonly SynchronizationContext mContext;
        private readonly object mSync = new object();
        private Queue<ServerThreadTask> mPendingNodes = new Queue<ServerThreadTask>();
        private Queue<ServerThreadTask> mCurrentNodes = new Queue<ServerThreadTask>();
        private State mState = State.Running;
        private int mLockCount;

        public ServerThread()
        {
            var caller = Thread.CurrentThread;
            mThread = new Thread(ExecutionThread);
            mThread.Name = "Server Thread";
            mCurrentCulture = caller.CurrentCulture;
            mCurrentUICulture = caller.CurrentUICulture;
            mContext = new ServerContext(this);
            CoreEvents.Log.ServerThreadCreated();
            mThread.Start();
        }

        public void Dispose()
        {
            lock (mSync)
            {
                if (mState == State.Running)
                {
                    mState = State.Shutdown;
                    CoreEvents.Log.ServerThreadBeginShutdown();
                    Monitor.Pulse(mSync);
                }
            }
        }

        public void DisposeAndJoin()
        {
            Dispose();

            if (IsCurrent)
            {
                for (;;)
                {
                    while (mCurrentNodes.Count != 0)
                        mCurrentNodes.Dequeue().Execute();

                    lock (mSync)
                    {
                        if (mPendingNodes.Count == 0)
                        {
                            if (mLockCount == 0)
                                mState = State.Terminated;

                            break;
                        }

                        Utils.Swap(ref mPendingNodes, ref mCurrentNodes);
                    }
                }
            }
            else
            {
                mThread.Join();
            }
        }

        public void Join()
        {
            if (!IsCurrent)
                mThread.Join();
        }

        public bool IsCurrent
        {
            get { return Thread.CurrentThread == mThread; }
        }

        public SynchronizationContext Context
        {
            get { return mContext; }
        }

        internal void IncrementUsageCount()
        {
            lock (mSync)
            {
                if (mState == State.Terminated)
                    throw new InvalidOperationException();

                checked { mLockCount++; }
            }
        }

        internal void DecrementUsageCount()
        {
            lock (mSync)
            {
                if (mLockCount <= 0)
                    throw new InvalidOperationException();

                if (--mLockCount == 0)
                    Monitor.Pulse(mSync);
            }
        }

        internal void PostServerTask(ServerThreadTask node)
        {
            lock (mSync)
            {
                if (mState == State.Terminated)
                    throw new InvalidOperationException();

                mPendingNodes.Enqueue(node);
                if (mPendingNodes.Count == 1)
                    Monitor.Pulse(mSync);
            }
        }

        private void ExecutionThread()
        {
            try
            {
                mThread.CurrentCulture = mCurrentCulture;
                mThread.CurrentUICulture = mCurrentUICulture;

                SynchronizationContext.SetSynchronizationContext(mContext);

                for (;;)
                {
                    lock (mSync)
                    {
                        while (mPendingNodes.Count == 0)
                        {
                            if (mState != State.Running)
                            {
                                if (mState == State.Shutdown && mLockCount == 0)
                                    mState = State.Terminated;

                                if (mState == State.Terminated)
                                {
                                    CoreEvents.Log.ServerThreadShutdownComplete();
                                    return;
                                }
                            }

                            CoreEvents.Log.ServerThreadWaitingForTasks();
                            Monitor.Wait(mSync);
                        }

                        Utils.Swap(ref mPendingNodes, ref mCurrentNodes);
                        CoreEvents.Log.ServerThreadFetchedTasks(mCurrentNodes.Count);
                    }

                    while (mCurrentNodes.Count != 0)
                        mCurrentNodes.Dequeue().Execute();
                }
            }
            catch (Exception ex)
            {
                Utils.ReportException(ex, true);
            }
        }
    }
}
