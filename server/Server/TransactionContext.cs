using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    public abstract class TransactionContextObj
    {
        private List<Action> mActions = new List<Action>();
        private bool mComplete;

        public abstract ContextManager Manager { get; }

        protected virtual void NotifyCompletionCommit(TransactionContextObj outer)
        {
            Utils.Assert(!mComplete);
            mComplete = true;

            if (outer == null)
                Manager.AssertGoingInactive();
            else if (outer.mActions == null)
                outer.mActions = mActions;
            else
                outer.mActions.AddRange(mActions);

            mActions = null;
        }

        protected virtual void NotifyCompletionRollback()
        {
            Utils.Assert(!mComplete);
            mComplete = true;

            for (int i = mActions.Count - 1; i >= 0; i--)
                mActions[i]();

            mActions = null;
        }

        public void Record(Action rollback)
        {
            AssertCurrentTransaction();
            Utils.Assert(rollback != null);
            Utils.Assert(!mComplete);
            mActions.Add(rollback);
        }

        internal abstract ContextMode GetMode();

        internal abstract TransactionContextObj OuterContext { get; }

        internal void AssertCurrentTransaction()
        {
            if (Manager.CurrentTransaction.ContextObject != this)
                throw new AssertionFailedException("This transaction is not the current transaction.");
        }
    }

    public readonly ref struct TransactionContext
    {
        private readonly TransactionContextObj mContext;
        internal TransactionContext(TransactionContextObj context) => mContext = context;
        internal TransactionContextObj ContextObject => mContext;
    }
}
