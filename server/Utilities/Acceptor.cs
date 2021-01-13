using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace AppNamespace
{
    public sealed class Acceptor : IDisposable
    {
        private SynchronizationContext mContext;
        private Socket mAcceptor;
        private SocketAsyncEventArgs mAcceptEvent;
        private Action<Socket> mOnAccept;
        private int mPort;

        public Acceptor(IPAddress ip, int port, Action<Socket> onAccept)
        {
            if (onAccept == null)
                throw new ArgumentNullException(nameof(onAccept));

            var endpoint = new IPEndPoint(ip, port);

            mContext = Utils.RequireContext();
            mOnAccept = onAccept;
            mPort = port;

            mAcceptEvent = new SocketAsyncEventArgs();
            mAcceptEvent.Completed += HandleAccept;

            mAcceptor = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try { mAcceptor.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); }
            catch (SocketException e)
            {
                if (e.NativeErrorCode != (int)SocketError.OperationNotSupported)
                    throw;
            }
            mAcceptor.Bind(endpoint);
            mAcceptor.Listen((int)SocketOptionName.MaxConnections);

            CoreEvents.Log.AcceptorStarted(mPort);

            if (!mAcceptor.AcceptAsync(mAcceptEvent))
                HandleAccept(mAcceptor, mAcceptEvent);
        }

        public void Dispose()
        {
            CoreEvents.Log.AcceptorStopped(mPort);

            mAcceptor.Dispose();
            mAcceptEvent.Dispose();
        }

        public int Port
        {
            get { return mPort; }
        }

        private void HandleAccept(object sender, SocketAsyncEventArgs e)
        {
            mContext.Post(x => HandleAccept(), null);
        }

        private void HandleAccept()
        {
        repeat:
            if (mAcceptEvent.SocketError == SocketError.Success)
            {
                var socket = mAcceptEvent.AcceptSocket;
                mAcceptEvent.AcceptSocket = null;
                CoreEvents.Log.AcceptorAccept(mPort, socket);
                mOnAccept(socket);
                if (!mAcceptor.AcceptAsync(mAcceptEvent))
                    goto repeat;
            }
            else
            {
                CoreEvents.Log.AcceptorError(mPort, mAcceptEvent.SocketError);
                ConsoleAccess.Flush();
                Environment.Exit(0);
            }
        }
    }
}
