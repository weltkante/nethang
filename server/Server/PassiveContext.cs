using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    public abstract class PassiveContextObj : TransactionContextObj
    {
        private readonly ContextManager mManager;
        private bool mComplete;
        private List<RundownContextCallback> mCompletionRoutines;

        internal PassiveContextObj(ContextManager manager)
        {
            mManager = manager;
        }

        private void HandleCompletion()
        {
            Utils.Assert(!mComplete);
            mComplete = true;

            if (mCompletionRoutines != null)
            {
                var rundownContext = new RundownContextObj(this);
                foreach (var callback in mCompletionRoutines)
                    callback(new RundownContext(rundownContext));
                rundownContext.Complete();
            }

            mCompletionRoutines = null;
        }

        protected override void NotifyCompletionCommit(TransactionContextObj outer)
        {
            base.NotifyCompletionCommit(outer);
            HandleCompletion();
        }

        protected override void NotifyCompletionRollback()
        {
            base.NotifyCompletionRollback();
            HandleCompletion();
        }

        public override ContextManager Manager
        {
            get { return mManager; }
        }

        public virtual bool HasFailed { get { return false; } }

        public void OnPartialCompletion(bool runOnFailure, RundownContextCallback callback)
        {
            Utils.Assert(callback != null);
            Utils.Assert(!mComplete);

            if (!runOnFailure)
            {
                var realCallback = callback;
                callback = rx => {
                    if (!HasFailed)
                        realCallback(rx);
                };
            }

            if (mCompletionRoutines == null)
                mCompletionRoutines = new List<RundownContextCallback>();

            mCompletionRoutines.Add(callback);
        }
    }

    public readonly ref struct PassiveContext
    {
        private readonly PassiveContextObj mContext;
        internal PassiveContextObj ContextObject => mContext;
        internal PassiveContext(PassiveContextObj context) => mContext = context;
        internal void AssertCurrentTransaction() => mContext.AssertCurrentTransaction();
        public ContextManager Manager => mContext.Manager;
        public void Record(Action rollback) => mContext.Record(rollback);
    }

    public delegate void RundownContextCallback(RundownContext tx);

    public sealed class RundownContextObj : PassiveContextObj
    {
        private PassiveContextObj mOuter;
        private bool mComplete;

        internal RundownContextObj(PassiveContextObj outer)
            : base(outer.Manager)
        {
            mOuter = outer;
            Manager.EnterScope(outer, this);
        }

        internal void Complete()
        {
            Utils.Assert(!mComplete);
            mComplete = true;
            NotifyCompletionCommit(mOuter);
            Manager.LeaveScope(this, mOuter);
        }

        internal override ContextMode GetMode()
        {
            return ContextMode.Passive;
        }

        internal override TransactionContextObj OuterContext
        {
            get { return mOuter; }
        }
    }

    public readonly ref struct RundownContext
    {
        private readonly RundownContextObj mContext;
        internal RundownContext(RundownContextObj context) => mContext = context;
    }

    internal sealed class RealPassiveContextObj : PassiveContextObj
    {
        private bool mComplete;

        internal RealPassiveContextObj(ContextManager manager)
            : base(manager)
        {
            Manager.EnterScope(null, this);
        }

        internal override ContextMode GetMode()
        {
            return ContextMode.Passive;
        }

        internal override TransactionContextObj OuterContext
        {
            get { return null; }
        }

        internal bool IsComplete
        {
            get { return mComplete; }
        }

        internal void Complete()
        {
            if (!mComplete)
            {
                mComplete = true;

                if (HasFailed)
                    NotifyCompletionRollback();
                else
                    NotifyCompletionCommit(null);

                Manager.LeaveScope(this, null);
                Manager.NotifyComplete(this);
            }
        }
    }

    public readonly ref struct RealPassiveContext
    {
        private readonly RealPassiveContextObj mContext;
        internal RealPassiveContextObj ContextObject => mContext;
        internal RealPassiveContext(RealPassiveContextObj context) => mContext = context;
    }

    public sealed class PassiveScope : IDisposable
    {
        public static PassiveScope Begin()
        {
            return ContextManager.CurrentManager.BeginPassive();
        }

        private RealPassiveContextObj mContext;

        internal PassiveScope(RealPassiveContextObj context)
        {
            mContext = context;
        }

        public void Dispose()
        {
            if (mContext != null)
            {
                mContext.Complete();
                mContext = null;
            }
        }

        public PassiveContext Context
        {
            get { return new PassiveContext(mContext); }
        }

        public PassiveContextObj ContextObject
        {
            get { return mContext; }
        }
    }
}
