using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace DotBoxD.LookaheadCapacityProbe;

internal sealed class TransportPair : IAsyncDisposable
{
    private readonly IDisposable[] _resources;

    private TransportPair(CountingReadStream reader, Stream writer, params IDisposable[] resources)
    {
        Reader = reader;
        Writer = writer;
        _resources = resources;
    }

    public CountingReadStream Reader { get; }

    public Stream Writer { get; }

    public static async Task<TransportPair> CreateAsync(ProbeTransport transport)
    {
        return transport switch
        {
            ProbeTransport.NamedPipe => await CreateNamedPipeAsync().ConfigureAwait(false),
            ProbeTransport.Tcp => await CreateTcpAsync().ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
        };
    }

    public ValueTask DisposeAsync()
    {
        foreach (var resource in _resources)
        {
            resource.Dispose();
        }

        return default;
    }

    private static async Task<TransportPair> CreateNamedPipeAsync()
    {
        var pipeName = $"dotboxd-lookahead-{Guid.NewGuid():N}";
        var receiver = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var sender = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            var accepting = receiver.WaitForConnectionAsync();
            await sender.ConnectAsync().ConfigureAwait(false);
            await accepting.ConfigureAwait(false);
            return new TransportPair(
                new CountingReadStream(receiver),
                sender,
                sender,
                receiver);
        }
        catch
        {
            sender.Dispose();
            receiver.Dispose();
            throw;
        }
    }

    private static async Task<TransportPair> CreateTcpAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var sender = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            var accepting = listener.AcceptTcpClientAsync();
            await sender.ConnectAsync(endpoint.Address, endpoint.Port).ConfigureAwait(false);
            var receiver = await accepting.ConfigureAwait(false);
            receiver.NoDelay = true;
            return new TransportPair(
                new CountingReadStream(receiver.GetStream()),
                sender.GetStream(),
                sender,
                receiver);
        }
        catch
        {
            sender.Dispose();
            throw;
        }
    }
}

internal sealed class CountingReadStream(Stream inner) : Stream
{
    private long _pendingReadCount;
    private long _readCount;

    public long PendingReadCount => Interlocked.Read(ref _pendingReadCount);

    public long ReadCount => Interlocked.Read(ref _readCount);

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        Interlocked.Increment(ref _readCount);
        return inner.Read(buffer, offset, count);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _readCount);
        var pending = inner.ReadAsync(buffer, cancellationToken);
        if (!pending.IsCompleted)
        {
            Interlocked.Increment(ref _pendingReadCount);
        }

        return pending;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}

internal sealed class GatedWriter : IDisposable
{
    private readonly AutoResetEvent _completion = new(initialState: false);
    private readonly int _fragmentLength;
    private readonly AutoResetEvent _request = new(initialState: false);
    private readonly Stream _stream;
    private readonly Thread _thread;
    private readonly byte[] _writeBuffer;
    private ExceptionDispatchInfo? _failure;
    private int _disposed;
    private long _startedAt;

    public GatedWriter(Stream stream, byte[] writeBuffer, int fragmentLength)
    {
        _stream = stream;
        _writeBuffer = writeBuffer;
        _fragmentLength = fragmentLength;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = nameof(GatedWriter),
        };
        _thread.Start();
    }

    public void Release()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _request.Set();
    }

    public long WaitForCompletion()
    {
        _completion.WaitOne();
        _failure?.Throw();
        return Volatile.Read(ref _startedAt);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _request.Set();
        _thread.Join();
        _request.Dispose();
        _completion.Dispose();
    }

    private void Run()
    {
        while (true)
        {
            _request.WaitOne();
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                Volatile.Write(ref _startedAt, Stopwatch.GetTimestamp());
                for (var offset = 0; offset < _writeBuffer.Length; offset += _fragmentLength)
                {
                    var length = Math.Min(_fragmentLength, _writeBuffer.Length - offset);
                    _stream.Write(_writeBuffer.AsSpan(offset, length));
                    if (_fragmentLength < _writeBuffer.Length)
                    {
                        Thread.Yield();
                    }
                }

                _stream.Flush();
            }
            catch (Exception exception)
            {
                _failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                _completion.Set();
            }

            if (_failure is not null)
            {
                return;
            }
        }
    }
}
