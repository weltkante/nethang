using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace AppNamespace
{
    [Serializable]
    public class ProtocolException : Exception
    {
        public static void Assert(bool condition)
        {
            if (!condition)
                throw new ProtocolException();
        }

        public ProtocolException() { }
        public ProtocolException(string message) : base(message) { }
        public ProtocolException(Exception inner) : base(null, inner) { }
        public ProtocolException(string message, Exception inner) : base(message, inner) { }
        protected ProtocolException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
