using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    public abstract class UplinkProtocol : IDisposable
    {
        private UplinkBase mUplink;
        private bool mDisposed;

        public void Dispose()
        {
            if (!mDisposed)
            {
                mDisposed = true;

                try { HandleDispose(); }
                catch (Exception ex)
                { Utils.ReportException(ex, true); }

                if (mUplink != null)
                {
                    mUplink.Dispose();
                    mUplink = null;
                }
            }
        }

        public UplinkBase Uplink
        {
            get { return mUplink; }
        }

        protected virtual void HandleDispose() { }
        protected virtual void HandleAttach() { }
        protected virtual void HandleDetach() { }
        protected abstract void ProcessInput(NetworkReader p);

        internal void AttachInternal(UplinkBase uplink)
        {
            Utils.Assert(uplink != null);
            Utils.Assert(mUplink == null);
            mUplink = uplink;
            try { HandleAttach(); }
            catch (Exception ex)
            { Utils.ReportException(ex, true); }
        }

        internal void DetachInternal()
        {
            Utils.Assert(mUplink != null);
            try { HandleDetach(); }
            catch (Exception ex)
            { Utils.ReportException(ex, true); }
            mUplink = null;
        }

        internal void ProcessInputInternal(NetworkReader data)
        {
            Utils.Assert(data != null);
            Utils.Assert(mUplink != null);
            try { ProcessInput(data); }
            catch (Exception ex)
            { Utils.ReportException(ex, true); }
        }
    }

    public abstract class UplinkBase : IDisposable, INetworkWriterTarget
    {
        private static int mNextLogId;

        private SynchronizationContext mContext;
        private UplinkProtocol mProtocol;
        private int mLogId;
        private bool mDisposed;

        private NetworkReaderSource mInputController;
        private NetworkWriterControl mOutputController;
        private MemoryStream mWriteBuffer = new MemoryStream();
        private byte[] mWriteFrame = new byte[1 << 10];
        private bool mShutdownNotificationPosted;
        private bool mPostedReadyForInput;

        protected UplinkBase()
        {
            mLogId = Interlocked.Increment(ref mNextLogId);
            CoreEvents.Log.UplinkConnected(this);

            mContext = Utils.RequireContext();
            mContext.OperationStarted();

            mOutputController = new NetworkWriterControl(this);
            mInputController = new NetworkReaderSource();
        }

        public void Dispose()
        {
            if (!mDisposed)
            {
                mDisposed = true;

                try
                {
                    HandleDispose();

                    Utils.Assert(mInputController.IsDisposed);
                    Utils.Assert(mOutputController.IsDisposed);

                    if (mProtocol != null)
                    {
                        mProtocol.DetachInternal();
                        mProtocol.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Utils.ReportException(ex, true);
                }

                mContext.OperationCompleted();
            }
        }

        protected virtual void HandleDispose()
        {
            mInputController.Dispose();
            mOutputController.Dispose();
        }

        public bool IsDisposed
        {
            get { return mDisposed; }
        }

        public SynchronizationContext Context
        {
            get { return mContext; }
        }

        public int LogId
        {
            get { return mLogId; }
        }

        public UplinkProtocol SetProtocol(UplinkProtocol protocol, bool disposeOldProtocol)
        {
            if (mProtocol == protocol)
                return null;

            var oldProtocol = mProtocol;
            if (oldProtocol != null)
            {
                mProtocol = null;
                oldProtocol.DetachInternal();

                if (disposeOldProtocol)
                {
                    oldProtocol.Dispose();
                    oldProtocol = null;
                }
            }

            if (protocol != null)
            {
                mProtocol = protocol;
                protocol.AttachInternal(this);
                ReadyForInput();
            }

            return oldProtocol;
        }

        public NetworkWriter Write()
        {
            return mOutputController.Write();
        }

        public void Close(bool hard)
        {
            NotifyShutdown();
        }

        private void NotifyShutdown()
        {
            if (!mDisposed && !mShutdownNotificationPosted)
            {
                mShutdownNotificationPosted = true;
                mContext.OperationStarted();
                mContext.Post(x => ((UplinkBase)x).HandleShutdown(), this);
            }
        }

        private void HandleShutdown()
        {
            if (!mDisposed)
            {
                mInputController.SetComplete();
                using (var p = mInputController.Read())
                {
                    try { mProtocol.ProcessInputInternal(p); }
                    catch (ProtocolException ex)
                    {
                        CoreEvents.Log.UplinkProtocolException(mLogId, ex);
                        Dispose();
                    }
                }
            }

            mContext.OperationCompleted();
        }

        protected void AppendInput(byte[] buffer, int offset, int length)
        {
            CoreEvents.Log.UplinkReceivedData(LogId, length);
            mInputController.Append(buffer, 0, length);
        }

        protected void CompleteInput()
        {
            CoreEvents.Log.UplinkRemoteShutdown(LogId);
            mInputController.SetComplete();
        }

        protected void ProcessInput()
        {
            if (!mPostedReadyForInput)
            {
                using (var p = mInputController.Read())
                {
                    try { mProtocol.ProcessInputInternal(p); }
                    catch (ProtocolException ex)
                    {
                        CoreEvents.Log.UplinkProtocolException(mLogId, ex);
                        Dispose();
                    }
                }
            }
        }

        public bool InputComplete
        {
            get { return mInputController.IsComplete; }
        }

        public int AvailableInput
        {
            get { return mInputController.Available; }
        }

        public void ReadyForInput()
        {
            if (!mPostedReadyForInput)
            {
                mPostedReadyForInput = true;
                mContext.OperationStarted();
                mContext.Post(ReadyForInputCallback, null);
            }
        }

        private void ReadyForInputCallback(object unused)
        {
            Utils.Assert(mPostedReadyForInput);
            mPostedReadyForInput = false;
            mContext.OperationCompleted();

            using (var p = mInputController.Read())
                mProtocol.ProcessInputInternal(p);
        }

        protected abstract void HandleSend(byte[] buffer, int offset, int length);

        public void BeginOutput()
        {
        }

        public ArraySegment<byte> RequestOutputPage(int sizeHint)
        {
            return new ArraySegment<byte>(mWriteFrame);
        }

        public void SubmitOutputPage(ArraySegment<byte> data)
        {
            mWriteBuffer.Write(data.Array, data.Offset, data.Count);
        }

        public void SuspendOutput()
        {
        }

        public void FlushOutput()
        {
            var buffer = mWriteBuffer.ToArray();
            HandleSend(buffer, 0, buffer.Length);
            mWriteBuffer.SetLength(0);
        }

        void INetworkWriterTarget.Begin()
        {
            BeginOutput();
        }

        void INetworkWriterTarget.Submit(ArraySegment<byte> data)
        {
            SubmitOutputPage(data);
        }

        void INetworkWriterTarget.Complete()
        {
            SuspendOutput();
        }
    }

    public sealed class SocketUplink : UplinkBase
    {
        public static SocketUplink Connect(IPEndPoint endpoint)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(endpoint);
            return new SocketUplink(socket);
        }

        private Socket mSocket;
        private IPEndPoint mRemote;
        private SocketAsyncEventArgs mRecvEvent;
        private byte[] mInputBuffer;
        private SocketAsyncEventArgs mSendEvent;
        private List<ArraySegment<byte>> mSendData;
        private bool mSendActive;

        public SocketUplink(Socket socket)
        {
            if ((mSocket = socket) == null)
                throw new ArgumentNullException(nameof(socket));

            mRemote = (IPEndPoint)mSocket.RemoteEndPoint;

            mSendEvent = new SocketAsyncEventArgs();
            mSendEvent.Completed += HandleSendComplete;

            mRecvEvent = new SocketAsyncEventArgs();
            mRecvEvent.Completed += HandleRecvComplete;

            mInputBuffer = new byte[1 << 10];
            mRecvEvent.SetBuffer(mInputBuffer, 0, mInputBuffer.Length);

            Context.OperationStarted();
            if (!mSocket.ReceiveAsync(mRecvEvent))
                Context.Post(x => ((SocketUplink)x).HandleRecvComplete(), this);
        }

        protected override void HandleDispose()
        {
            mSocket.Dispose();
            mRecvEvent.Dispose();
            mSendEvent.Dispose();
            base.HandleDispose();
        }

        public IPEndPoint RemoteEndpoint
        {
            get { return mRemote; }
        }

        private void HandleRecvComplete(object sender, SocketAsyncEventArgs e)
        {
            Context.Post(x => ((SocketUplink)x).HandleRecvComplete(), this);
        }

        private void HandleRecvComplete()
        {
            bool process = false;

        repeat:
            Context.OperationCompleted();

            if (mRecvEvent.SocketError == SocketError.Success)
            {
                process = true;
                int length = mRecvEvent.BytesTransferred;
                if (length == 0)
                {
                    CompleteInput();
                }
                else
                {
                    AppendInput(mInputBuffer, 0, length);
                    mRecvEvent.SetBuffer(mInputBuffer, 0, mInputBuffer.Length);

                    Context.OperationStarted();
                    if (!mSocket.ReceiveAsync(mRecvEvent))
                        goto repeat;
                }
            }
            else
            {
                process = false;
                CoreEvents.Log.UplinkError(LogId, mRecvEvent.SocketError);
                Dispose();
            }

            if (process)
                ProcessInput();
        }

        protected override void HandleSend(byte[] buffer, int offset, int length)
        {
            try
            {
                if (mSendData == null)
                    mSendData = new List<ArraySegment<byte>>();

                mSendData.Add(new ArraySegment<byte>(buffer, offset, length));

                if (!mSendActive)
                {
                    mSendEvent.BufferList = mSendData;
                    mSendData = null;
                    mSendActive = true;

                    Context.OperationStarted();
                    if (!mSocket.SendAsync(mSendEvent))
                        HandleSendComplete();
                }
            }
            catch (SocketException ex)
            {
                CoreEvents.Log.UplinkError(LogId, ex.SocketErrorCode);
                Dispose();
            }
        }

        private void HandleSendComplete(object sender, SocketAsyncEventArgs e)
        {
            Context.Post(x => ((SocketUplink)x).HandleSendComplete(), this);
        }

        private void HandleSendComplete()
        {
        repeat:
            Context.OperationCompleted();
            mSendActive = false;

            if (mSendEvent.SocketError != SocketError.Success)
            {
                CoreEvents.Log.UplinkError(LogId, mSendEvent.SocketError);
                Dispose();
                return;
            }

            if (mSendData != null && mSendData.Count > 0)
            {
                mSendEvent.BufferList = mSendData;
                mSendData = null;
                mSendActive = true;

                Context.OperationStarted();
                if (!mSocket.SendAsync(mSendEvent))
                    goto repeat;
            }
        }
    }
}
