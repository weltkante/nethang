using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    public sealed class NetworkReaderSource : IDisposable
    {
        private NetworkReader mReader;
        private bool mReading;
        private bool mComplete;

        private byte[] mBuffer;
        private int mOffset;
        private int mEnding;

        public NetworkReaderSource()
        {
            mReader = new NetworkReader(this);
            mBuffer = new byte[1 << 10];
        }

        public void Dispose()
        {
            if (mReader != null)
            {
                mReader.DisposeInternal();
                mReader = null;
            }
        }

        public bool IsDisposed
        {
            get { return mReader == null; }
        }

        public void Append(byte[] buffer, int offset, int length)
        {
            Utils.Assert(!mReading);

            if (length > mBuffer.Length - mEnding)
            {
                byte[] temp = mBuffer;
                if (length > mBuffer.Length - (mEnding - mOffset))
                    temp = new byte[Math.Max(mBuffer.Length * 2, length + (mEnding - mOffset))];

                Buffer.BlockCopy(mBuffer, mOffset, temp, 0, mEnding - mOffset);
                mBuffer = temp;
                mEnding -= mOffset;
                mOffset = 0;
            }

            Buffer.BlockCopy(buffer, offset, mBuffer, mEnding, length);
            mEnding += length;
        }

        public void SetComplete()
        {
            mComplete = true;
        }

        public bool IsComplete
        {
            get { return mComplete; }
        }

        public int Available
        {
            get
            {
                Utils.Assert(!mReading);
                return mEnding - mOffset;
            }
        }

        public NetworkReader Read()
        {
            Utils.Assert(!mReading);
            mReading = true;
            mReader.Initialize(mBuffer, mOffset, mEnding, mComplete);
            return mReader;
        }

        internal void NotifyReadComplete(byte[] buffer, int offset, int ending)
        {
            Utils.Assert(mReading);
            Utils.Assert(mBuffer == buffer);
            Utils.Assert(ending - offset <= mEnding - mOffset);
            mOffset = offset;
            mEnding = ending;
            mReading = false;
        }
    }

    public sealed class NetworkReader : IDisposable
    {
        public static NetworkReader FromBuffer(byte[] buffer, int offset, int length)
        {
            var source = new NetworkReaderSource();
            source.Append(buffer, offset, length);
            source.SetComplete();
            return source.Read();
        }

        private static readonly byte[] kNoScratchBuffer = new byte[0];

        private NetworkReaderSource mSource;
        private readonly Encoding mEncoding = Encoding.UTF8;
        private byte[] mScratchBuffer = kNoScratchBuffer;

        private byte[] mBuffer;
        private int mOffset;
        private int mEnding;
        private bool mComplete;

        internal NetworkReader(NetworkReaderSource source)
        {
            mSource = source;
        }

        internal void Initialize(byte[] buffer, int offset, int ending, bool complete)
        {
            Utils.Assert(mSource != null);
            Utils.Assert(mBuffer == null);
            mBuffer = buffer;
            mOffset = offset;
            mEnding = ending;
            mComplete = complete;
        }

        internal void DisposeInternal()
        {
            mSource = null;
            mBuffer = null;
            mOffset = 0;
            mEnding = 0;
            mComplete = false;
        }

        public void Dispose()
        {
            if (mBuffer != null)
            {
                mSource.NotifyReadComplete(mBuffer, mOffset, mEnding);

                mBuffer = null;
                mOffset = 0;
                mEnding = 0;
                mComplete = false;
            }
        }

        private void EnsureScratchBuffer(int capacity)
        {
            if (mScratchBuffer.Length < capacity)
                mScratchBuffer = new byte[Math.Max(mScratchBuffer.Length * 2, capacity)];
        }

        public int Remaining
        {
            get { return mEnding - mOffset; }
        }

        public bool Complete
        {
            get { return mComplete; }
        }

        public struct NetworkReaderStack : IDisposable
        {
            private NetworkReader mReader;
            private int mCookie;
            private int mEnding;
            private bool mComplete;

            internal NetworkReaderStack(NetworkReader reader, int cookie, int ending, bool complete)
            {
                mReader = reader;
                mCookie = cookie;
                mEnding = ending;
                mComplete = complete;
            }

            public void Dispose()
            {
                if (mReader != null)
                {
                    mReader.RestoreInput(mCookie, mEnding, mComplete);
                    mReader = null;
                    mCookie = 0;
                    mEnding = 0;
                    mComplete = false;
                }
            }
        }

        public NetworkReaderStack ConstrainInput(int length)
        {
            Utils.Assert(0 <= length && length <= Remaining);
            var result = new NetworkReaderStack(this, 1, mEnding, mComplete);
            mComplete = (length == Remaining);
            mEnding = mOffset + length;
            return result;
        }

        private void RestoreInput(int cookie, int ending, bool complete)
        {
            if (cookie != 1)
                throw new InvalidOperationException();

            mEnding = ending;
            mComplete = complete;
        }

        private void ReadBytesInternal(byte[] buffer, int offset, int length)
        {
            if (length > mEnding - mOffset)
                throw new ProtocolException();

            Buffer.BlockCopy(mBuffer, mOffset, buffer, offset, length);
            mOffset += length;
        }

        public byte ReadByte()
        {
            if (mOffset == mEnding)
                throw new ProtocolException();

            return mBuffer[mOffset++];
        }

        public ushort ReadUInt16()
        {
            int value = ReadByte();
            value |= ReadByte() << 8;
            return (ushort)value;
        }

        public uint ReadUInt32()
        {
            int value = ReadByte();
            value |= ReadByte() << 8;
            value |= ReadByte() << 16;
            value |= ReadByte() << 24;
            return (uint)value;
        }

        public uint ReadPackedUInt32()
        {
            uint value = 0;
            int shift = 0;

            int part = ReadByte();
            while (part >= 0x80)
            {
                if (shift == 28)
                    throw new ProtocolException();

                value += (uint)(part & 0x7F) << shift;
                shift += 7;
                part = ReadByte();
            }

            if (shift == 28 && part > 15)
                throw new ProtocolException();

            value += (uint)part << shift;
            return value;
        }

        public bool ReadBool()
        {
            byte value = ReadByte();
            ProtocolException.Assert(value <= 1);
            return value == 1;
        }

        public int ReadInt32()
        {
            int value = ReadByte();
            value |= ReadByte() << 8;
            value |= ReadByte() << 16;
            value |= ReadByte() << 24;
            return value;
        }

        public string ReadString(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return String.Empty;
            EnsureScratchBuffer(length);
            ReadBytesInternal(mScratchBuffer, 0, length);
            try { return mEncoding.GetString(mScratchBuffer, 0, length); }
            catch (Exception ex) { throw new ProtocolException(ex); }
        }

        public string ReadString()
        {
            int length = (int)ReadPackedUInt32();
            if (length < 0) throw new ProtocolException();
            return ReadString(length);
        }

        public byte[] ReadByteArray()
        {
            int length = (int)ReadPackedUInt32();
            if (length < 0) throw new ProtocolException();
            byte[] data = new byte[length];
            ReadBytesInternal(data, 0, length);
            return data;
        }
    }
}
