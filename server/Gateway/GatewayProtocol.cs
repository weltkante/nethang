using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppNamespace
{
    public abstract class GatewayProtocolWrapper: UplinkProtocol, INetworkWriterTarget
    {
        public sealed class Server: GatewayProtocolWrapper
        {
            private GatewayProtocol.IGatewayServer mServer;

            public Server(GatewayProtocol.IGatewayServer server)
            {
                mServer = server;
            }

            protected override void HandleDispose()
            {
                base.HandleDispose();
                mServer.Disconnect(true);
            }

            protected override void ProcessFrame(NetworkReader p)
            {
                while(p.Remaining > 0)
                    GatewayProtocol.ParseClientPacket(p, mServer);
            }
        }

        public sealed class Client: GatewayProtocolWrapper
        {
            private GatewayProtocol.IGatewayClient mClient;

            public Client(GatewayProtocol.IGatewayClient client)
            {
                mClient = client;
            }

            protected override void HandleDispose()
            {
                base.HandleDispose();
                mClient.Disconnect(true);
            }

            protected override void ProcessFrame(NetworkReader p)
            {
                while(p.Remaining > 0)
                    GatewayProtocol.ParseServerPacket(p, mClient);
            }
        }

        private NetworkWriterControl mOutput;
        private int mNextBlockSize;
        private bool mPostedFlush;

        private GatewayProtocolWrapper()
        {
            mOutput = new NetworkWriterControl(this);
        }

        protected override void HandleDispose()
        {
            base.HandleDispose();
            mOutput.Dispose();
        }

        protected abstract void ProcessFrame(NetworkReader p);

        public NetworkWriter BeginPacket()
        {
            Utils.Assert(!mOutput.IsDisposed);
            return mOutput.Write();
        }

        public void Flush()
        {
            Uplink.BeginOutput();
            Uplink.SubmitOutputPage(new ArraySegment<byte>(BitConverter.GetBytes(mTotal)));
            foreach (var page in mPages) Uplink.SubmitOutputPage(page);
            Uplink.SuspendOutput();
            Uplink.FlushOutput();
            mPages.Clear();
            mTotal = 0;
        }

        protected sealed override void ProcessInput(NetworkReader p)
        {
            for(; ; )
            {
                if(mNextBlockSize == 0)
                {
                    if(p.Remaining < 4)
                    {
                        if (p.Complete)
                        {
                            ProtocolException.Assert(p.Remaining == 0);
                            Uplink.Close(false);
                        }

                        return;
                    }
                    else
                    {
                        mNextBlockSize = p.ReadInt32();
                        Utils.Assert(mNextBlockSize > 0);
                    }
                }
                else
                {
                    if(p.Remaining < mNextBlockSize)
                    {
                        ProtocolException.Assert(!p.Complete);
                        return;
                    }
                    else
                    {
                        using(p.ConstrainInput(mNextBlockSize))
                        {
                            ProcessFrame(p);
                            Utils.Assert(p.Remaining == 0);
                            mNextBlockSize = 0;
                        }
                    }
                }
            }
        }

        private List<ArraySegment<byte>> mPages = new List<ArraySegment<byte>>();
        private int mTotal;

        void INetworkWriterTarget.Begin()
        {
        }

        void INetworkWriterTarget.Submit(ArraySegment<byte> data)
        {
            mPages.Add(data);
            mTotal += data.Count;
        }

        void INetworkWriterTarget.Complete()
        {
            if(!mPostedFlush)
            {
                mPostedFlush = true;
                SynchronizationContext.Current.Post(x => {
                    mPostedFlush = false;
                    Flush();
                }, null);
            }
        }
    }

    public static class GatewayProtocol
    {
        public const ushort kPort = 3719;

        public const byte kAckConnectionCheck = 0x80;
        public const byte kDoActivate = 0x10;
        public const byte kDoReady = 0x11;
        public const byte kDoDeactivate = 0x12;
        public const byte kDoSetState = 0x20;
        public const byte kDoProcess = 0x21;
        public const byte kDoSendData = 0x22;
        public const byte kDoTerm = 0x23;
        public const byte kDoKill = 0x24;
        public const byte kDoCommit = 0x30;

        public interface IGatewayServer
        {
            void Disconnect(bool hard);
            void AckConnectionCheck();
            void DoActivate(int port);
            void DoReady(int port);
            void DoDeactivate(int port);
            void DoSetState(int clientId, byte[] data);
            void DoProcess(int clientId, int length);
            void DoSendData(int clientId, ArraySegment<byte> data);
            void DoTerm(int clientId);
            void DoKill(int clientId);
            void DoCommit(int clientId);
        }

        public static void ParseClientPacket(NetworkReader p, IGatewayServer c)
        {
            GatewayServer.RequireServerThread();

            var pid = p.ReadByte();
            switch(pid)
            {
            case kAckConnectionCheck: ParseAckConnectionCheck(p, c); break;
            case kDoActivate: ParseDoActivate(p, c); break;
            case kDoReady: ParseDoReady(p, c); break;
            case kDoDeactivate: ParseDoDeactivate(p, c); break;
            case kDoSetState: ParseDoSetState(p, c); break;
            case kDoProcess: ParseDoProcess(p, c); break;
            case kDoSendData: ParseDoSendData(p, c); break;
            case kDoTerm: ParseDoTerm(p, c); break;
            case kDoKill: ParseDoKill(p, c); break;
            case kDoCommit: ParseDoCommit(p, c); break;
            default: throw new ProtocolException($"Unknown packet id: 0x{pid:X2}");
            }
        }

        public static void SendAckConnectionCheck(NetworkWriter p)
        {
            p.WriteByte(kAckConnectionCheck);
        }

        public static void ParseAckConnectionCheck(NetworkReader p, IGatewayServer c)
        {
            c.AckConnectionCheck();
        }

        public static void SendDoActivate(NetworkWriter p, int port)
        {
            p.WriteByte(kDoActivate);
            p.WriteUInt16((ushort)port);
        }

        public static void ParseDoActivate(NetworkReader p, IGatewayServer c)
        {
            var port = p.ReadUInt16();
            c.DoActivate(port);
        }

        public static void SendDoReady(NetworkWriter p, int port)
        {
            p.WriteByte(kDoReady);
            p.WriteUInt16((ushort)port);
        }

        public static void ParseDoReady(NetworkReader p, IGatewayServer c)
        {
            var port = p.ReadUInt16();
            c.DoReady(port);
        }

        public static void ParseDoDeactivate(NetworkReader p, IGatewayServer c)
        {
            var port = p.ReadUInt16();
            c.DoDeactivate(port);
        }

        public static void SendDoSetState(NetworkWriter p, int clientId, ArraySegment<byte> data)
        {
            p.WriteByte(kDoSetState);
            p.WriteInt32(clientId);
            p.WriteByteArray(data);
        }

        public static void ParseDoSetState(NetworkReader p, IGatewayServer c)
        {
            var clientId = p.ReadInt32();
            ProtocolException.Assert(clientId > 0);
            var data = p.ReadByteArray();
            c.DoSetState(clientId, data);
        }

        public static void SendDoProcess(NetworkWriter p, int clientId, int length)
        {
            p.WriteByte(kDoProcess);
            p.WriteInt32(clientId);
            p.WriteInt32(length);
        }

        public static void ParseDoProcess(NetworkReader p, IGatewayServer c)
        {
            var clientId = p.ReadInt32();
            ProtocolException.Assert(clientId > 0);
            var length = p.ReadInt32();
            ProtocolException.Assert(length > 0);
            c.DoProcess(clientId, length);
        }

        public static void ParseDoSendData(NetworkReader p, IGatewayServer c)
        {
            var clientId = p.ReadInt32();
            ProtocolException.Assert(clientId > 0);
            var data = p.ReadByteArray();
            c.DoSendData(clientId, new ArraySegment<byte>(data));
        }

        public static void SendDoTerm(NetworkWriter p, int clientId)
        {
            p.WriteByte(kDoTerm);
            p.WriteInt32(clientId);
        }

        public static void ParseDoTerm(NetworkReader p, IGatewayServer c)
        {
            var clientId = p.ReadInt32();
            ProtocolException.Assert(clientId > 0);
            c.DoTerm(clientId);
        }

        public static void SendDoKill(NetworkWriter p, int clientId)
        {
            p.WriteByte(kDoKill);
            p.WriteInt32(clientId);
        }

        public static void ParseDoKill(NetworkReader p, IGatewayServer c)
        {
            var clientId = p.ReadInt32();
            ProtocolException.Assert(clientId > 0);
            c.DoKill(clientId);
        }

        public static void SendDoCommit(NetworkWriter p, int clientId)
        {
            p.WriteByte(kDoCommit);
            p.WriteInt32(clientId);
        }

        public static void ParseDoCommit(NetworkReader p, IGatewayServer c)
        {
            var clientId = p.ReadInt32();
            ProtocolException.Assert(clientId > 0);
            c.DoCommit(clientId);
        }

        public const byte kCheckConnection = 0x80;
        public const byte kCheckConnection2 = 0x81;
        public const byte kOnActivate = 0x10;
        public const byte kOnDeactivate = 0x11;
        public const byte kOnClientInit = 0x20;
        public const byte kOnClientData = 0x21;
        public const byte kOnClientTerm = 0x22;
        public const byte kOnClientDead = 0x23;

        public interface IGatewayClient
        {
            void Disconnect(bool hard);
            void CheckConnection();
            void CheckConnection2();
            void OnActivate(int port, bool success);
            void OnDeactivate(int port);
            void OnClientInit(int clientId, bool complete, string endpoint, ArraySegment<byte> state, ArraySegment<byte> data);
            void OnClientData(int clientId, ArraySegment<byte> data);
            void OnClientTerm(int clientId);
            void OnClientDead(int clientId);
        }

        public static void ParseServerPacket(NetworkReader p, IGatewayClient c)
        {
            var pid = p.ReadByte();
            switch(pid)
            {
            case kCheckConnection: ParseCheckConnection(p, c); break;
            case kCheckConnection2: ParseCheckConnection2(p, c); break;
            case kOnActivate: ParseOnActivate(p, c); break;
            case kOnDeactivate: ParseOnDeactivate(p, c); break;
            case kOnClientInit: ParseOnClientInit(p, c); break;
            case kOnClientData: ParseOnClientData(p, c); break;
            case kOnClientTerm: ParseOnClientTerm(p, c); break;
            case kOnClientDead: ParseOnClientDead(p, c); break;
            default: throw new ProtocolException($"Unknown packet id: 0x{pid:X2}");
            }
        }

        public static void SendCheckConnection(NetworkWriter p)
        {
            p.WriteByte(kCheckConnection);
        }

        public static void SendCheckConnection2(NetworkWriter p)
        {
            p.WriteByte(kCheckConnection2);
        }

        public static void ParseCheckConnection(NetworkReader p, IGatewayClient c)
        {
            c.CheckConnection();
        }

        public static void ParseCheckConnection2(NetworkReader p, IGatewayClient c)
        {
            c.CheckConnection2();
        }

        public static void SendOnActivate(NetworkWriter p, int port, bool success)
        {
            p.WriteByte(kOnActivate);
            p.WriteUInt16((ushort)port);
            p.WriteBool(success);
        }

        public static void ParseOnActivate(NetworkReader p, IGatewayClient c)
        {
            var port = p.ReadUInt16();
            var success = p.ReadBool();
            c.OnActivate(port, success);
        }

        public static void ParseOnDeactivate(NetworkReader p, IGatewayClient c)
        {
            var port = p.ReadUInt16();
            c.OnDeactivate(port);
        }

        public static void SendOnClientInit(NetworkWriter p, int clientId, bool complete, IPEndPoint endpoint, ArraySegment<byte> state, ArraySegment<byte> data)
        {
            p.WriteByte(kOnClientInit);
            p.WriteInt32(clientId);
            p.WriteBool(complete);
            p.WriteString(endpoint.ToString());
            p.WriteByteArray(state);
            p.WriteByteArray(data);
        }

        public static void ParseOnClientInit(NetworkReader p, IGatewayClient c)
        {
            var clientId = p.ReadInt32();
            var complete = p.ReadBool();
            ProtocolException.Assert(clientId > 0);
            var endpoint = p.ReadString();
            var state = new ArraySegment<byte>(p.ReadByteArray());
            var data = new ArraySegment<byte>(p.ReadByteArray());
            c.OnClientInit(clientId, complete, endpoint, state, data);
        }

        public static void SendOnClientData(NetworkWriter p, int clientId, ArraySegment<byte> data)
        {
            p.WriteByte(kOnClientData);
            p.WriteInt32(clientId);
            p.WriteByteArray(data);
        }

        public static void ParseOnClientData(NetworkReader p, IGatewayClient c)
        {
            var clientId = p.ReadInt32();
            ProtocolException.Assert(clientId > 0);
            var data = new ArraySegment<byte>(p.ReadByteArray());
            c.OnClientData(clientId, data);
        }

        public static void SendOnClientTerm(NetworkWriter p, int clientId)
        {
            p.WriteByte(kOnClientTerm);
            p.WriteInt32(clientId);
        }

        public static void ParseOnClientTerm(NetworkReader p, IGatewayClient c)
        {
            var clientId = p.ReadInt32();
            ProtocolException.Assert(clientId > 0);
            c.OnClientTerm(clientId);
        }

        public static void SendOnClientDead(NetworkWriter p, int clientId)
        {
            p.WriteByte(kOnClientDead);
            p.WriteInt32(clientId);
        }

        public static void ParseOnClientDead(NetworkReader p, IGatewayClient c)
        {
            var clientId = p.ReadInt32();
            ProtocolException.Assert(clientId > 0);
            c.OnClientDead(clientId);
        }
    }
}
