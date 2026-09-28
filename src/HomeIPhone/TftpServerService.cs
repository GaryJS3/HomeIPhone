using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace HomeIPhone;

public sealed class TftpServerService(PhoneService phones, IOptions<PhoneServerOptions> options, ILogger<TftpServerService> logger) : BackgroundService
{
    private UdpClient? listener;
    private readonly ConcurrentDictionary<string, Task> transfers = new();
    public bool Bound { get; private set; }
    public int BoundPort => (listener?.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        listener = new UdpClient(new IPEndPoint(IPAddress.Parse(options.Value.TftpBindAddress), options.Value.TftpPort));
        Bound = true;
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var request = await listener!.ReceiveAsync(stoppingToken);
                var key = request.RemoteEndPoint.ToString();
                if (transfers.TryGetValue(key, out var existing) && !existing.IsCompleted) continue;
                foreach (var done in transfers.Where(p => p.Value.IsCompleted)) transfers.TryRemove(done.Key, out _);
                if (transfers.Count >= 32) continue;
                transfers[key] = Handle(request, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            Bound = false;
            listener?.Dispose();
            await Task.WhenAll(transfers.Values);
        }
    }

    public static bool SafeFilename(string filename) => filename.Length is > 0 and <= 200
        && filename.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
        && !filename.Contains("..") && filename[0] != '.';

    private async Task Handle(UdpReceiveResult request, CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Parse(options.Value.TftpBindAddress), 0));
        long? eventId = null;
        long transferred = 0;
        string? mac = null;
        int? version = null;
        try
        {
            var data = request.Buffer;
            if (data.Length < 4 || data.Length > 1024 || data[0] != 0 || data[1] != 1)
            {
                await Error(udp, request.RemoteEndPoint, 4, "Read requests only", cancellationToken);
                return;
            }
            var parts = Encoding.ASCII.GetString(data, 2, data.Length - 2).Split('\0');
            if (parts.Length < 3 || !parts[1].Equals("octet", StringComparison.OrdinalIgnoreCase) || data[^1] != 0 || !SafeFilename(parts[0]))
            {
                await Error(udp, request.RemoteEndPoint, 4, "Invalid filename or mode; use octet", cancellationToken);
                return;
            }
            var filename = parts[0];
            eventId = await phones.RecordRequest(filename, request.RemoteEndPoint.Address.ToString());
            mac = Mac.FromFilename(filename);
            Stream? stream = null;
            if (mac is not null && filename.Equals($"SEP{mac}.cnf.xml", StringComparison.OrdinalIgnoreCase))
            {
                var phone = await phones.Get(mac);
                if (phone is not null)
                {
                    version = phone.ConfigVersion;
                    stream = new MemoryStream(Encoding.UTF8.GetBytes(PhoneConfigGenerator.Generate(mac, phone.Configuration)));
                }
            }
            // Flat files only, and no symbolic links (including the static directory itself).
            var directory = new DirectoryInfo(Path.Combine(options.Value.DataPath, "tftp"));
            var file = new FileInfo(Path.Combine(directory.FullName, filename));
            if (stream is null && file.Exists && !directory.Attributes.HasFlag(FileAttributes.ReparsePoint) && !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                stream = file.OpenRead();
            }
            if (stream is null)
            {
                await Error(udp, request.RemoteEndPoint, 1, "File not found", cancellationToken);
                await phones.FinishTransfer(eventId.Value, "File not found", 0, mac, null);
                return;
            }
            await using (stream)
            {
                ushort block = 1;
                var packet = new byte[516];
                packet[1] = 3;
                while (true)
                {
                    var count = await stream.ReadAtLeastAsync(packet.AsMemory(4, 512), 512, false, cancellationToken);
                    BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), block);
                    var acknowledged = false;
                    for (var attempt = 0; attempt < 5 && !acknowledged; attempt++)
                    {
                        await udp.SendAsync(packet.AsMemory(0, count + 4), request.RemoteEndPoint, cancellationToken);
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(1));
                        try
                        {
                            while (true)
                            {
                                var ack = await udp.ReceiveAsync(timeout.Token);
                                if (!ack.RemoteEndPoint.Equals(request.RemoteEndPoint))
                                {
                                    await Error(udp, ack.RemoteEndPoint, 5, "Unknown transfer ID", timeout.Token);
                                    continue;
                                }
                                if (ack.Buffer.Length >= 2 && ack.Buffer[0] == 0 && ack.Buffer[1] == 5) throw new IOException("Client aborted transfer");
                                if (ack.Buffer.Length == 4 && ack.Buffer[0] == 0 && ack.Buffer[1] == 4 && BinaryPrimitives.ReadUInt16BigEndian(ack.Buffer.AsSpan(2)) == block)
                                {
                                    acknowledged = true;
                                    break;
                                }
                            }
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                    }
                    if (!acknowledged) throw new IOException("ACK timeout");
                    transferred += count;
                    if (count < 512) break;
                    block = unchecked((ushort)(block + 1));
                }
            }
            await phones.FinishTransfer(eventId.Value, "Completed", transferred, mac, version);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TFTP transfer from {Endpoint} failed", request.RemoteEndPoint);
            if (eventId.HasValue) await phones.FinishTransfer(eventId.Value, "Failed: " + ex.Message, transferred, mac, null);
        }
    }

    private static ValueTask<int> Error(UdpClient udp, IPEndPoint endpoint, byte code, string message, CancellationToken token)
        => udp.SendAsync(new byte[] { 0, 5, 0, code }.Concat(Encoding.ASCII.GetBytes(message + "\0")).ToArray().AsMemory(), endpoint, token);
}
