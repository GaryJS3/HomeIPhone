using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeIPhone;

public static class CiscoXmlParser
{
    public static Dictionary<string, string> Flatten(string xml)
    {
        var document = PhoneConfigGenerator.Parse(xml);
        var result = new Dictionary<string, string>();
        void Visit(XElement element, string path)
        {
            foreach (var attribute in element.Attributes()) result[path + "/@" + attribute.Name] = attribute.Value;
            if (!element.HasElements) result[path] = element.Value;
            var counts = new Dictionary<XName, int>();
            foreach (var child in element.Elements())
            {
                counts.TryGetValue(child.Name, out var index);
                counts[child.Name] = ++index;
                Visit(child, path + "/" + child.Name + (index > 1 ? $"[{index}]" : ""));
            }
        }
        if (document.Root is not null) Visit(document.Root, document.Root.Name.ToString());
        return result;
    }
}

public sealed record EndpointSnapshot(bool Success, string? Error, Dictionary<string, string> Fields);

public sealed class CiscoPhoneHttpClient(HttpClient http)
{
    public static readonly string[] Endpoints = ["DeviceInformationX", "NetworkConfigurationX", "EthernetInformationX",
        "PortInformationX?1", "PortInformationX?2", "PortInformationX?3", "DeviceLogX?0", "DeviceLogX?1", "DeviceLogX?2",
        "StreamingStatisticsX?1", "StreamingStatisticsX?2", "StreamingStatisticsX?3", "StreamingStatisticsX?4", "StreamingStatisticsX?5"];

    public static bool AllowedIp(string value)
    {
        if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    public async Task<Dictionary<string, EndpointSnapshot>> Poll(string ip, CancellationToken token)
    {
        if (!AllowedIp(ip)) throw new ArgumentException("Polling requires a private IPv4 LAN address learned through TFTP.");
        var result = new Dictionary<string, EndpointSnapshot>();
        foreach (var endpoint in Endpoints)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                using var response = await http.GetAsync($"http://{IPAddress.Parse(ip)}/{endpoint}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
                {
                    if (buffer.Length + read > 131072) throw new IOException("Response exceeds 128 KiB");
                    buffer.Write(chunk, 0, read);
                }
                result[endpoint] = new(true, null, CiscoXmlParser.Flatten(System.Text.Encoding.UTF8.GetString(buffer.ToArray())));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException or OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                result[endpoint] = new(false, ex is OperationCanceledException ? "Timed out" : ex.Message, []);
            }
        }
        return result;
    }
}

public sealed class PhonePoller(PhoneService phones, CiscoPhoneHttpClient client)
{
    private readonly SemaphoreSlim gate = new(4, 4);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> active = new();
    public async Task Poll(string mac, CancellationToken token = default)
    {
        mac = Mac.Normalize(mac);
        if (!active.TryAdd(mac, 0)) return;
        try { await gate.WaitAsync(token); } catch { active.TryRemove(mac, out _); throw; }
        try
        {
            var phone = await phones.Get(mac) ?? throw new KeyNotFoundException("Phone not found.");
            if (phone.LastKnownIp is null) throw new ArgumentException("No IP learned yet. Wait for a TFTP request.");
            var snapshot = await client.Poll(phone.LastKnownIp, token);
            await phones.Use(async db =>
            {
                var current = await db.Phones.FindAsync(phone.MacAddress);
                if (current is null || current.LastKnownIp != phone.LastKnownIp) return false;
                var now = DateTime.UtcNow;
                var online = snapshot.Values.Any(s => s.Success);
                if (current.Online != online || (!online && current.LastHttpFailureUtc is null))
                    PhoneService.AddEvent(db, current.MacAddress, "HTTP", online ? "Device became reachable" : "Device unreachable; inspect endpoint diagnostics.");
                current.Online = online;
                if (online)
                {
                    current.LastHttpSuccessUtc = now;
                    current.LastSeenUtc = now;
                }
                else current.LastHttpFailureUtc = now;
                var previous = JsonSerializer.Deserialize<Dictionary<string, EndpointSnapshot>>(current.SnapshotJson) ?? [];
                foreach (var item in snapshot.Where(s => !s.Value.Success))
                {
                    if (!previous.TryGetValue(item.Key, out var old) || old.Error != item.Value.Error)
                        PhoneService.AddEvent(db, current.MacAddress, "HTTP", item.Key + ": " + item.Value.Error);
                }
                var firmware = snapshot["DeviceInformationX"].Fields.FirstOrDefault(f => f.Key.Contains("appLoad", StringComparison.OrdinalIgnoreCase)).Value;
                if (firmware is not null && firmware != current.Firmware)
                {
                    PhoneService.AddEvent(db, current.MacAddress, "FW", $"App load changed {current.Firmware ?? "unknown"} → {firmware}");
                    current.Firmware = firmware;
                }
                current.SnapshotJson = JsonSerializer.Serialize(snapshot);
                current.SnapshotUtc = now;
                await db.SaveChangesAsync(token);
                return true;
            });
        }
        finally { gate.Release(); active.TryRemove(mac, out _); }
    }
}

public sealed class PhonePollingService(PhoneService phones, PhonePoller poller, IOptions<PhoneServerOptions> options, ILogger<PhonePollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Parallel.ForEachAsync((await phones.List()).Where(p => p.LastKnownIp is not null),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = stoppingToken }, async (phone, token) =>
                {
                    try { await poller.Poll(phone.MacAddress, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (Exception ex) { logger.LogWarning(ex, "Polling {Mac} failed", phone.MacAddress); }
                });
            await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken);
        }
    }
}

public sealed class HistoryCleanupService(PhoneService phones, IOptions<PhoneServerOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var cutoff = DateTime.UtcNow.AddHours(-options.Value.HistoryRetentionHours);
            await phones.Use(async db =>
            {
                await db.Events.Where(e => e.TimestampUtc < cutoff).ExecuteDeleteAsync(stoppingToken);
                await db.TftpEvents.Where(e => e.TimestampUtc < cutoff).ExecuteDeleteAsync(stoppingToken);
                return true;
            });
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}
