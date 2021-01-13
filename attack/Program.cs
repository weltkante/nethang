using System;
using System.Buffers;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        for (int i = 0; i < 10; i++)
            await Task.WhenAll(Enumerable.Range(0, 20).Select(j => Connect()));
    }

    static async Task Connect()
    {
        var data = new ReadOnlyMemory<byte>(new byte[1]);
        for (int j = 0; j < 10; j++)
        {
            try
            {
                using (var sock = new Socket(SocketType.Stream, ProtocolType.IP))
                {
                    sock.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
                    await sock.ConnectAsync("127.0.0.1", 3720);

                    // send data for 200 milliseconds
                    var start = DateTime.UtcNow;
                    do { _ = sock.SendAsync(data, SocketFlags.None); }
                    while ((DateTime.UtcNow - start).TotalMilliseconds < 200);
                }
            }
            catch
            {
            }
        }
    }
}
