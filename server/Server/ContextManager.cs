using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    public enum ContextMode { Inactive, Passive }

    public sealed class ContextManager
    {
        [ThreadStatic]
        private static ContextManager mCurrentManager;

        public static ContextManager CurrentManager
        {
            get { return mCurrentManager; }
        }

        internal static void Initialize()
        {
            if (mCurrentManager != null)
                throw new InvalidOperationException("This thread already has a ContextManager.");

            mCurrentManager = new ContextManager();
        }

        private ContextMode mMode;
        private RealPassiveContextObj mContext;
        private TransactionContextObj mCurrent;
        private ImmutableList<Action> mTopLevelCompletion = ImmutableList<Action>.Empty;

        private ContextManager()
        {
            mMode = ContextMode.Inactive;
        }

        public PassiveScope BeginPassive()
        {
            if (mMode != ContextMode.Inactive)
                throw new InvalidOperationException();

            Utils.Assert(mContext == null);

            mContext = new RealPassiveContextObj(this);
            return new PassiveScope(mContext);
        }

        internal void EnterScope(TransactionContextObj current, TransactionContextObj next)
        {
            Utils.Assert(mCurrent == current);
            Utils.Assert(mMode == (current != null ? current.GetMode() : ContextMode.Inactive));
            mCurrent = next;
            mMode = next.GetMode();
        }

        internal void LeaveScope(TransactionContextObj current, TransactionContextObj next)
        {
            Utils.Assert(mCurrent == current);
            Utils.Assert(mMode == current.GetMode());
            mCurrent = next;
            mMode = (next != null ? next.GetMode() : ContextMode.Inactive);
        }

        internal void NotifyComplete(RealPassiveContextObj context)
        {
            Utils.Assert(mContext == context);
            Utils.Assert(mCurrent == null);
            Utils.Assert(mMode == ContextMode.Inactive);
            mContext = null;

            while (!mTopLevelCompletion.IsEmpty)
            {
                var callback = mTopLevelCompletion[0];
                mTopLevelCompletion = mTopLevelCompletion.RemoveAt(0);
                callback();
            }
        }

        internal RealPassiveContext GetRealTransaction()
        {
            Utils.Assert(mContext != null);
            return new RealPassiveContext(mContext);
        }

        internal void AssertGoingInactive()
        {
            Utils.Assert(mCurrent != null);
            Utils.Assert(mCurrent == mContext);
            Utils.Assert(mContext.IsComplete);
        }

        public TransactionContext CurrentTransaction
        {
            get { return new TransactionContext(mCurrent); }
        }
    }
}
