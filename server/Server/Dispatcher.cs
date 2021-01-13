using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    partial class ClientProtocol
    {
        internal void ProcessData(ArraySegment<byte> data, bool complete)
        {
            if (data.Count > 0)
                mInputBuffer.Append(data.Array, data.Offset, data.Count);

            if (complete)
                mInputBuffer.SetComplete();

            using (var tx = PassiveScope.Begin())
            using (var p = mInputBuffer.Read())
            {
                int processed = 0;
                ProcessData(tx, p, ref processed);
                Utils.Assert(!tx.ContextObject.HasFailed);
                mOutputBuffer.CompleteProcessing(tx.Context, processed);
            }

            mOutputBuffer.AssertNoFlushRequired();
        }

        private void ProcessData(PassiveScope tx, NetworkReader p, ref int processed)
        {
            for (; ; )
            {
                if (mInputBlockSize == 0)
                {
                    if (p.Remaining < 3)
                    {
                        if (p.Complete)
                        {
                            ProtoCheck(p.Remaining == 0);
                            Disconnect(tx.Context);
                            return;
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        mHeaderBlockSize = 2;
                        mInputBlockSize = p.ReadUInt16() + 1;
                        mRemotePacketCount = p.ReadByte();
                        mHeaderBlockSize += 1;
                        mClient.UpdateClientSequenceId(tx, mRemotePacketCount);
                    }
                }
                else
                {
                    if (p.Remaining < mInputBlockSize)
                    {
                        ProtoCheck(!p.Complete);
                        return;
                    }
                    else
                    {
                        using (p.ConstrainInput(mInputBlockSize))
                        {
                            ProcessPackets(tx, p);
                            Utils.Assert(p.Remaining == 0);
                            processed += mInputBlockSize + mHeaderBlockSize;
                            mInputBlockSize = 0;
                            mHeaderBlockSize = 0;
                        }
                    }
                }
            }
        }

        private void ProcessPackets(PassiveScope tx, NetworkReader p)
        {
            while (p.Remaining > 0)
                ProcessPacket(p);
        }

        private void ProcessPacket(NetworkReader p)
        {
            byte pid = p.ReadByte();
            System.Threading.Thread.Sleep(10);
            throw new ProtocolException($"Invalid packet id: 0x{pid:X2}");
        }
    }
}
