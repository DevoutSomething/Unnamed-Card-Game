using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Game.Net
{
    /// <summary>
    /// One TCP connection, framed as [4-byte little-endian length][UTF8 JSON].
    /// Reads happen on a dedicated background thread and land in a thread-safe
    /// queue; call Pump() from a MonoBehaviour's Update() to dispatch OnMessage/
    /// OnDisconnected on the main thread, where it's safe to touch GameState,
    /// UI, etc. Sends can be called from the main thread directly.
    /// </summary>
    public class NetChannel : IDisposable
    {
        private const int MaxFrameBytes = 16 * 1024 * 1024;

        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly Thread _readThread;
        private readonly ConcurrentQueue<NetMessage> _inbox = new ConcurrentQueue<NetMessage>();
        private readonly object _writeLock = new object();
        private volatile bool _closed;
        private string _pendingDisconnectReason;
        private bool _disconnectReported;

        public event Action<NetMessage> OnMessage;
        public event Action<string> OnDisconnected;

        public NetChannel(TcpClient tcp)
        {
            _tcp = tcp;
            _tcp.NoDelay = true;
            _stream = tcp.GetStream();
            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "NetChannel-Read" };
            _readThread.Start();
        }

        public void Send(NetMessage msg)
        {
            if (_closed) return;
            byte[] body = Encoding.UTF8.GetBytes(NetJson.Serialize(msg));
            byte[] header = BitConverter.GetBytes(body.Length);
            try
            {
                lock (_writeLock)
                {
                    _stream.Write(header, 0, header.Length);
                    _stream.Write(body, 0, body.Length);
                }
            }
            catch (Exception e)
            {
                QueueDisconnect(e.Message);
            }
        }

        /// <summary>Call once per frame from the main thread.</summary>
        public void Pump()
        {
            while (_inbox.TryDequeue(out var msg))
                OnMessage?.Invoke(msg);

            if (_pendingDisconnectReason != null && !_disconnectReported)
            {
                _disconnectReported = true;
                OnDisconnected?.Invoke(_pendingDisconnectReason);
            }
        }

        private void ReadLoop()
        {
            try
            {
                byte[] header = new byte[4];
                while (!_closed)
                {
                    if (!ReadExact(header, 4)) { QueueDisconnect("connection closed"); return; }
                    int length = BitConverter.ToInt32(header, 0);
                    if (length <= 0 || length > MaxFrameBytes)
                        throw new IOException($"invalid frame length {length}");

                    byte[] body = new byte[length];
                    if (!ReadExact(body, length)) { QueueDisconnect("connection closed"); return; }

                    var json = Encoding.UTF8.GetString(body);
                    var msg = NetJson.Deserialize<NetMessage>(json);
                    _inbox.Enqueue(msg);
                }
            }
            catch (Exception e)
            {
                if (!_closed) QueueDisconnect(e.Message);
            }
        }

        private bool ReadExact(byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0) return false;
                offset += read;
            }
            return true;
        }

        private void QueueDisconnect(string reason)
        {
            _closed = true;
            _pendingDisconnectReason = reason;
        }

        public void Dispose()
        {
            _closed = true;
            try { _stream?.Close(); } catch { /* already gone */ }
            try { _tcp?.Close(); } catch { /* already gone */ }
        }
    }
}
