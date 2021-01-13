using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    internal sealed class GatewayClientConnection
    {
        private static readonly byte[] kNoState = new byte[0];
        private const int kDefaultInputBufferSize = 1 << 10;

        private SynchronizationContext mContext;
        private GatewayPort mGateway;
        private Socket mSocket;
        private SocketAsyncEventArgs mRecvEvent;
        private IPEndPoint mRemote;
        private byte[] mState;
        private byte[] mInputBuffer;
        private int mClientId;

        private int mProcessedOffset;
        private int mPublishedOffset;
        private int mInputEnding;
        private bool mDetectedClientDisconnect;
        private bool mNotifiedClientDisconnect;
        private bool mProcessedDisconnect;
        private bool mDisposed;

        private byte[] mPendingState;
        private List<ArraySegment<byte>> mPendingSend = new List<ArraySegment<byte>>();
        private int mPendingProcessed;
        private bool mPendingDisconnect;

        private SocketAsyncEventArgs mSendEvent;
        private List<ArraySegment<byte>> mSendData;
        private bool mSendActive;
        private bool mSendDisconnect;

        public GatewayClientConnection(GatewayPort gateway, Socket socket, int clientId)
        {
            mContext = Utils.RequireContext();
            mClientId = clientId;
            mGateway = gateway;
            mSocket = socket;
            mSocket.SendTimeout = (int)TimeSpan.FromSeconds(4).TotalMilliseconds;
            mRemote = (IPEndPoint)mSocket.RemoteEndPoint;
            mState = kNoState;
            mInputBuffer = new byte[kDefaultInputBufferSize];
            mSendEvent = new SocketAsyncEventArgs();
            mSendEvent.Completed += HandleSendComplete;
            mRecvEvent = new SocketAsyncEventArgs();
            mRecvEvent.Completed += HandleRecvComplete;
            mRecvEvent.SetBuffer(mInputBuffer, 0, mInputBuffer.Length);

            if(mGateway.IsReady)
                mGateway.Controller.NotifyClientConnect(this);

            if(!mSocket.ReceiveAsync(mRecvEvent))
                mContext.Post(x => HandleRecvComplete(), null);
        }

        private void HandleRecvComplete(object sender, SocketAsyncEventArgs e)
        {
            mContext.Post(x => HandleRecvComplete(), null);
        }

        private void HandleRecvComplete()
        {
            GatewayServer.RequireServerThread();

            if (mDisposed) return;

            var requiresProcessing = false;

        repeat:
            if(mRecvEvent.SocketError == SocketError.Success)
            {
                requiresProcessing = true;

                var length = mRecvEvent.BytesTransferred;
                if(length == 0)
                {
                    GatewayEvents.Log.ClientRemoteDisconnected(mClientId);
                    mDetectedClientDisconnect = true;
                }
                else
                {
                    GatewayEvents.Log.ClientReceivedData(mClientId, length);
                    mInputEnding += length;

                    if(mProcessedOffset > 0)
                    {
                        Buffer.BlockCopy(mInputBuffer, mProcessedOffset, mInputBuffer, 0, mInputEnding - mProcessedOffset);
                        mInputEnding -= mProcessedOffset;
                        mPublishedOffset -= mProcessedOffset;
                        mProcessedOffset = 0;
                    }

                    if(mInputEnding > mInputBuffer.Length / 2)
                        Array.Resize(ref mInputBuffer, mInputBuffer.Length * 2);

                    Utils.Assert(!mDisposed);
                    mRecvEvent.SetBuffer(mInputBuffer, mInputEnding, mInputBuffer.Length - mInputEnding);
                    if(!mSocket.ReceiveAsync(mRecvEvent))
                        goto repeat;
                }
            }
            else
            {
                requiresProcessing = false;

                if(!mDisposed)
                {
                    Dispose();
                    mGateway.NotifyClientDead(this);
                }
            }

            if(requiresProcessing && mGateway.IsReady)
            {
                if(mInputEnding > mPublishedOffset)
                {
                    Utils.Assert(!mNotifiedClientDisconnect);
                    mGateway.Controller.NotifyClientData(this, new ArraySegment<byte>(mInputBuffer, mPublishedOffset, mInputEnding - mPublishedOffset));
                    mPublishedOffset = mInputEnding;
                }

                if(mDetectedClientDisconnect && !mNotifiedClientDisconnect)
                {
                    mNotifiedClientDisconnect = true;
                    mGateway.Controller.NotifyClientRemoteShutdown(this);
                }
            }
        }

        private void HandleSendComplete(object sender, SocketAsyncEventArgs e)
        {
            mContext.Post(x => HandleSendComplete(), null);
        }

        private void HandleSendComplete()
        {
            GatewayServer.RequireServerThread();

            if (!mDisposed)
            {
            repeat:
                mSendActive = false;

                if (mSendEvent.SocketError == SocketError.Success)
                {
                    if (mSendData != null && mSendData.Count > 0)
                    {
                        mSendEvent.BufferList = mSendData;
                        mSendData = null;
                        mSendActive = true;

                        if (!mSocket.SendAsync(mSendEvent))
                            goto repeat;
                    }

                    if (!mSendActive && !mSendDisconnect && mProcessedDisconnect)
                    {
                        try
                        {
                            mSendDisconnect = true;
                            mSocket.Shutdown(SocketShutdown.Send);
                        }
                        catch (SocketException)
                        {
                            Dispose();
                            mGateway.NotifyClientDead(this);
                        }
                    }
                }
                else
                {
                    if (!mDisposed)
                    {
                        Dispose();
                        mGateway.NotifyClientDead(this);
                    }
                }
            }
        }

        public void Dispose()
        {
            GatewayServer.RequireServerThread();

            if(!mDisposed)
            {
                mDisposed = true;
                mProcessedOffset = 0;
                mPublishedOffset = 0;
                mInputEnding = 0;

                GatewayEvents.Log.ClientDisposed(mClientId);

                Rollback();
                mSocket.Close(0);
                mRecvEvent.Dispose();
            }
        }

        public GatewayPort Gateway
        {
            get { return mGateway; }
        }

        public int Id
        {
            get { return mClientId; }
        }

        public IPEndPoint RemoteEndpoint
        {
            get { return mRemote; }
        }

        public void WriteInitPacket(NetworkWriter p)
        {
            Utils.Assert(mPendingProcessed == 0);
            GatewayProtocol.SendOnClientInit(p, mClientId, mDetectedClientDisconnect, mRemote, new ArraySegment<byte>(mState), new ArraySegment<byte>(mInputBuffer, mProcessedOffset, mInputEnding - mProcessedOffset));
            mNotifiedClientDisconnect = mDetectedClientDisconnect;
            mPublishedOffset = mInputEnding;
        }

        public void TxSetState(byte[] data)
        {
            Utils.Assert(data != null);
            if(!mDisposed)
            {
                mPendingState = data;
            }
        }

        public void TxProcess(int length)
        {
            ProtocolException.Assert(length > 0);
            if(!mDisposed)
            {
                ProtocolException.Assert(length <= mPublishedOffset - mProcessedOffset - mPendingProcessed);
                mPendingProcessed += length;
            }
        }

        public void TxSend(ArraySegment<byte> data)
        {
            ProtocolException.Assert(data.Count > 0);
            if(!mDisposed)
            {
                ProtocolException.Assert(!mSendDisconnect);
                ProtocolException.Assert(!mProcessedDisconnect);
                ProtocolException.Assert(!mPendingDisconnect);

                mPendingSend.Add(data);
            }
        }

        public void TxDisconnect()
        {
            if(!mDisposed)
            {
                ProtocolException.Assert(!mSendDisconnect);
                ProtocolException.Assert(!mProcessedDisconnect);
                ProtocolException.Assert(!mPendingDisconnect);

                mPendingDisconnect = true;
            }
        }

        public void Rollback()
        {
            mPendingSend.Clear();
            mPendingState = null;
            mPendingProcessed = 0;
            mPendingDisconnect = false;
        }

        public void Commit()
        {
            GatewayServer.RequireServerThread();

            if(!mDisposed)
            {
                if(mPendingState != null)
                {
                    mState = mPendingState;
                    mPendingState = null;
                }

                Utils.Assert(0 <= mPendingProcessed && mPendingProcessed <= mPublishedOffset - mProcessedOffset);
                mProcessedOffset += mPendingProcessed;
                mPendingProcessed = 0;

                if(mPendingSend.Count > 0)
                {
                    GatewayEvents.Log.ClientSentData(mClientId, mPendingSend.Sum(x => x.Count));

                    if (mSendData == null)
                        mSendData = new List<ArraySegment<byte>>();

                    mSendData.AddRange(mPendingSend);
                    mPendingSend.Clear();

                    if (!mSendActive)
                    {
                        mSendActive = true;
                        mSendEvent.BufferList = mSendData;
                        mSendData = null;

                        if (!mSocket.SendAsync(mSendEvent))
                            HandleSendComplete();
                    }
                }

                if(mPendingDisconnect)
                {
                    GatewayEvents.Log.ClientGracefulDisconnect(mClientId);

                    mProcessedDisconnect = true;
                    mPendingDisconnect = false;

                    try
                    {
                        if (!mSendActive && !mSendDisconnect)
                        {
                            mSendDisconnect = true;
                            mSocket.Shutdown(SocketShutdown.Send);
                            mSocket.Close();
                            mRecvEvent.Dispose();
                            mGateway.NotifyClientTerm(this);
                        }
                    }
                    catch(SocketException)
                    {
                        Dispose();
                        mGateway.NotifyClientDead(this);
                        return;
                    }
                }
            }
        }
    }
}
