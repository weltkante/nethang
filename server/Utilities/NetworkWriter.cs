using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    public interface INetworkWriterTarget
    {
        void Begin();
        void Submit(ArraySegment<byte> data);
        void Complete();
    }

    public sealed class NetworkWriterControl : IDisposable
    {
        private INetworkWriterTarget mTarget;
        private NetworkWriter mWriter;
        private bool mWriting;

        public NetworkWriterControl(INetworkWriterTarget target)
        {
            mTarget = target;
            mWriter = new NetworkWriter(this);
        }

        public void Dispose()
        {
            if (mWriter != null)
            {
                mWriter.DisposeInternal();
                mWriter = null;
            }
        }

        public bool IsDisposed
        {
            get { return mWriter == null; }
        }

        public NetworkWriter Write()
        {
            Utils.Assert(!mWriting);
            mWriting = true;
            mTarget.Begin();
            mWriter.Initialize(new ArraySegment<byte>(new byte[16]));
            return mWriter;
        }

        internal void Complete()
        {
            Utils.Assert(mWriting);
            mWriting = false;
            mTarget.Complete();
        }

        internal void Submit(ArraySegment<byte> data)
        {
            Utils.Assert(mWriting);
            mTarget.Submit(data);
        }

        internal ArraySegment<byte> RequestBuffer(int sizeHint)
        {
            Utils.Assert(mWriting);
            return new ArraySegment<byte>(new byte[Math.Max(sizeHint, 64)]);
        }
    }

    public sealed class NetworkWriter : IDisposable
    {
        private static readonly byte[] kNoScratchBuffer = new byte[0];

        private NetworkWriterControl mControl;
        private readonly Encoding mEncoding = Encoding.UTF8;
        private byte[] mScratchBuffer = kNoScratchBuffer;

        private byte[] mBuffer;
        private int mOrigin;
        private int mOffset;
        private int mEnding;

        internal NetworkWriter(NetworkWriterControl control)
        {
            mControl = control;
        }

        internal void DisposeInternal()
        {
            mControl = null;
            mBuffer = null;
            mOrigin = 0;
            mOffset = 0;
            mEnding = 0;
        }

        public void Dispose()
        {
            if (mControl != null)
            {
                mControl.Submit(new ArraySegment<byte>(mBuffer, mOrigin, mOffset));
                mControl.Complete();
                mBuffer = null;
                mOrigin = 0;
                mOffset = 0;
                mEnding = 0;
            }
        }

        internal void Initialize(ArraySegment<byte> buffer)
        {
            Utils.Assert(mControl != null);
            Utils.Assert(mBuffer == null);

            mBuffer = buffer.Array;
            mOrigin = buffer.Offset;
            mOffset = mOrigin;
            mEnding = mOrigin + buffer.Count;

            Utils.Assert(mOffset <= mEnding);
        }

        private void RequestBuffer(int sizeHint)
        {
            mControl.Submit(new ArraySegment<byte>(mBuffer, mOrigin, mOffset));
            var buffer = mControl.RequestBuffer(sizeHint);
            mBuffer = buffer.Array;
            mOrigin = buffer.Offset;
            mOffset = mOrigin;
            mEnding = mOrigin + buffer.Count;
            Utils.Assert(mOffset < mEnding);
        }

        public void WriteBytes(byte[] data, int offset, int length)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (offset < 0 || offset > data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (length < 0 || length > data.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length));

            WriteBytesInternal(data, offset, length);
        }

        private void WriteBytesInternal(byte[] data, int offset, int length)
        {
            while (length > 0)
            {
                if (mOffset == mEnding)
                    RequestBuffer(length);

                int copy = Math.Min(length, mEnding - mOffset);
                Buffer.BlockCopy(data, offset, mBuffer, mOffset, copy);
                mOffset += copy;
                offset += copy;
                length -= copy;
            }
        }

        public void WriteByte(byte value)
        {
            if (mOffset == mEnding)
            {
                RequestBuffer(1);
                Utils.Assert(mOffset < mEnding);
            }

            mBuffer[mOffset++] = value;
        }

        public void WriteBool(bool value)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteUInt16(ushort value)
        {
            WriteByte((byte)value);
            WriteByte((byte)(value >> 8));
        }

        public void WritePackedUInt32(uint value)
        {
            while (value >= 0x80)
            {
                WriteByte((byte)((byte)value | 0x80));
                value >>= 7;
            }

            WriteByte((byte)value);
        }

        public void WriteInt32(int value)
        {
            WriteByte((byte)value);
            WriteByte((byte)(value >> 8));
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 24));
        }

        public void WriteString(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                WriteByte(0);
                return;
            }

            byte[] encodedData = mEncoding.GetBytes(value);
            WritePackedUInt32((uint)encodedData.Length);
            WriteBytesInternal(encodedData, 0, encodedData.Length);
        }

        public void WriteByteArray(ArraySegment<byte> data)
        {
            WriteByteArray(data.Array, data.Offset, data.Count);
        }

        public void WriteByteArray(byte[] buffer, int offset, int length)
        {
            if (buffer == null)
            {
                if (offset == 0 && length == 0)
                {
                    WriteByte(0);
                    return;
                }

                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (length < 0 || length > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length));

            if (length == 0)
            {
                WriteByte(0);
                return;
            }

            WritePackedUInt32((uint)length);
            WriteBytesInternal(buffer, offset, length);
        }
    }
}
