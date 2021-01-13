using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    internal sealed class ClientOutputBuffer : INetworkWriterTarget
    {
        private readonly ClientConnection mClient;
        private readonly NetworkWriterControl mOutputControl;
        private ImmutableList<ArraySegment<byte>> mFlushedPages = ImmutableList<ArraySegment<byte>>.Empty;
        private ImmutableList<ArraySegment<byte>> mSubmittedPages = ImmutableList<ArraySegment<byte>>.Empty;
        private int mSubmittedLength;
        private int mPossibleFlushOffset;
        private int mPossibleFlushPage;
        private bool mWriting;
        private bool mShutdownPending;
        private int mProcessedPending;

        private TransactionContextObj mCurrentTransaction;
        private RealPassiveContextObj mRootTransaction;

        public ClientOutputBuffer(ClientConnection client)
        {
            Utils.Assert(client != null);
            mClient = client;
            mOutputControl = new NetworkWriterControl(this);
        }

        public void SendFlushedPages(ClientConnection client, GatewayConnector gateway, int processed, bool shutdown)
        {
            gateway.SendClientData(client, mFlushedPages, processed, shutdown);
            mFlushedPages = mFlushedPages.Clear();
        }

        internal void CompleteProcessing(PassiveContext tx, int processed)
        {
            Utils.Assert(!mWriting);
            if (!mClient.IsDisposed)
            {
                if (mCurrentTransaction != tx.ContextObject)
                    EnlistTransaction(tx);

                Utils.Assert(mProcessedPending == 0);
                tx.Record(() => mProcessedPending = 0);
                mProcessedPending = processed;
            }
        }

        public void Disconnect(PassiveContext tx)
        {
            Utils.Assert(!mWriting);
            Utils.Assert(!mShutdownPending);
            Utils.Assert(!mClient.IsDisposed);

            tx.AssertCurrentTransaction();

            if (mCurrentTransaction != tx.ContextObject)
                EnlistTransaction(tx);

            mShutdownPending = true;
        }

        internal void AssertNoFlushRequired()
        {
            Utils.Assert(!mWriting);
            Utils.Assert(mFlushedPages.IsEmpty);
            Utils.Assert(mSubmittedPages.IsEmpty);
        }

        private void EnlistTransaction(PassiveContext tx)
        {
            Utils.Assert(!mWriting);
            Utils.Assert(mCurrentTransaction != tx.ContextObject);

            var oldTransaction = mCurrentTransaction;
            var oldFlushedPages = mFlushedPages;
            var oldSubmittedPages = mSubmittedPages;
            var oldSubmittedLength = mSubmittedLength;
            var oldPossibleFlushOffset = mPossibleFlushOffset;
            var oldPossibleFlushPage = mPossibleFlushPage;
            var oldShutdownPending = mShutdownPending;

            tx.Record(() => {
                Utils.Assert(!mWriting);

                mFlushedPages = oldFlushedPages;
                mSubmittedPages = oldSubmittedPages;
                mSubmittedLength = oldSubmittedLength;
                mPossibleFlushOffset = oldPossibleFlushOffset;
                mPossibleFlushPage = oldPossibleFlushPage;
                mCurrentTransaction = oldTransaction;
                mShutdownPending = oldShutdownPending;
            });

            if (mRootTransaction == null)
            {
                mRootTransaction = tx.Manager.GetRealTransaction().ContextObject;
                mRootTransaction.OnPartialCompletion(true, rx => {
                    Utils.Assert(!mWriting);
                    if (mRootTransaction.HasFailed)
                    {
                        Utils.Assert(mSubmittedLength == 0);
                        Utils.Assert(mSubmittedPages.Count == 0);
                        Utils.Assert(mFlushedPages.Count == 0);
                        Utils.Assert(mCurrentTransaction == null);
                        Utils.Assert(!mShutdownPending);
                        Utils.Assert(mProcessedPending == 0);
                    }
                    else if (!mClient.IsDisposed)
                    {
                        if (mSubmittedLength > 0)
                            InternalFlush();

                        mClient.SendFlushedPages(this, mProcessedPending, mShutdownPending);
                        mProcessedPending = 0;
                        mCurrentTransaction = null;
                    }
                    mRootTransaction = null;
                });
                Utils.Assert(mProcessedPending == 0);
            }
            else
            {
                Utils.Assert(mRootTransaction == tx.Manager.GetRealTransaction().ContextObject);
            }

            mCurrentTransaction = tx.ContextObject;
        }

        private void WriteHeader(int blockLength)
        {
            blockLength -= 1;
            Utils.Assert(blockLength >= 0);
            if (blockLength < 0x8000)
            {
                var header = new byte[2];
                header[0] = (byte)blockLength;
                header[1] = (byte)(blockLength >> 8);
                mFlushedPages = mFlushedPages.Add(new ArraySegment<byte>(header));
            }
            else
            {
                var header = new byte[4];
                header[0] = (byte)blockLength;
                header[1] = (byte)((blockLength >> 8) | 0x80);
                header[2] = (byte)(blockLength >> 15);
                header[3] = (byte)(blockLength >> 23);
                mFlushedPages = mFlushedPages.Add(new ArraySegment<byte>(header));
            }
        }

        private void InternalFlush()
        {
            if (mSubmittedLength > 0)
            {
                WriteHeader(mSubmittedLength);
                mFlushedPages = mFlushedPages.AddRange(mSubmittedPages);
                mSubmittedPages = mSubmittedPages.Clear();
                mSubmittedLength = 0;
                mPossibleFlushOffset = 0;
                mPossibleFlushPage = 0;
            }
        }

        void INetworkWriterTarget.Begin()
        {
            Utils.Assert(!mWriting);
            mWriting = true;
        }

        void INetworkWriterTarget.Submit(ArraySegment<byte> data)
        {
            Utils.Assert(mWriting);

            if (data.Count == 0)
                return;

            mSubmittedPages = mSubmittedPages.Add(data);
            mSubmittedLength += data.Count;
        }

        void INetworkWriterTarget.Complete()
        {
            Utils.Assert(mWriting);
            mWriting = false;

            mPossibleFlushOffset = mSubmittedLength;
            mPossibleFlushPage = mSubmittedPages.Count;
        }
    }

    public sealed partial class ClientProtocol
    {
        private ClientConnection mClient;
        private NetworkReaderSource mInputBuffer;
        private ClientOutputBuffer mOutputBuffer;
        private int mHeaderBlockSize;
        private int mInputBlockSize;
        private uint mRemotePacketCount;
        private bool mGracefulDisconnect;

        public ClientProtocol(ClientConnection client)
        {
            mClient = client;
            mInputBuffer = new NetworkReaderSource();
            mOutputBuffer = new ClientOutputBuffer(client);
        }

        private void ProtoCheck(bool condition)
        {
            if (!condition)
                throw new ProtocolException();
        }

        public bool IsOffline
        {
            get { return mGracefulDisconnect || mClient.IsDisposed; }
        }

        public void Disconnect(PassiveContext tx)
        {
            if (IsOffline) return;
            mOutputBuffer.Disconnect(tx);
            Utils.Assert(!mGracefulDisconnect);
            tx.Record(() => mGracefulDisconnect = false);
            mGracefulDisconnect = true;
        }
    }
}
