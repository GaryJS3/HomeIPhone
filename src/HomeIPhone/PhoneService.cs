using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeIPhone;

public sealed class PhoneService(IDbContextFactory<Database> factory, DatabaseGate gate, IOptions<PhoneServerOptions> options)
{
    public async Task<T> Use<T>(Func<Database, Task<T>> action)
    {
        await gate.Semaphore.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            return await action(db);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    public Task<List<Phone>> List() => Use(db => db.Phones.AsNoTracking().OrderBy(p => p.MacAddress).ToListAsync());
    public Task<Phone?> Get(string mac) => Use(db => db.Phones.AsNoTracking().SingleOrDefaultAsync(p => p.MacAddress == Mac.Normalize(mac)));
    public Task<List<DiscoveryCandidate>> Discoveries() => Use(db => db.Discovered.AsNoTracking().OrderByDescending(p => p.LastSeenUtc).ToListAsync());
    public Task<List<PhoneEvent>> Events(string mac) => Use(db => db.Events.Where(e => e.MacAddress == Mac.Normalize(mac)).OrderByDescending(e => e.TimestampUtc).Take(200).ToListAsync());
    public Task<List<TftpEvent>> Requests(string? mac = null) => Use(db => db.TftpEvents.Where(e => mac == null || e.MacAddress == mac).OrderByDescending(e => e.TimestampUtc).Take(200).ToListAsync());

    public Task<Phone> Add(string mac, string? name, bool adopt = false) => Use(async db =>
    {
        mac = Mac.Normalize(mac);
        if (await db.Phones.AnyAsync(p => p.MacAddress == mac))
        {
            throw new InvalidOperationException("A phone with this MAC already exists.");
        }
        var candidate = await db.Discovered.FindAsync(mac);
        if (adopt && candidate is null)
        {
            throw new KeyNotFoundException("Discovery candidate no longer exists.");
        }
        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var phone = new Phone
        {
            MacAddress = mac,
            LastKnownIp = candidate?.LastKnownIp,
            FirstSeenUtc = candidate?.FirstSeenUtc,
            LastSeenUtc = candidate?.LastSeenUtc,
            LastTftpUtc = candidate?.LastSeenUtc,
            Configuration = new PhoneConfiguration
            {
                FriendlyName = string.IsNullOrWhiteSpace(name) ? "Phone " + mac[^6..] : name,
                ServicesUrl = baseUrl + "/phone/services",
                DirectoryUrl = baseUrl + "/phone/directory",
                IdleUrl = baseUrl + "/phone/idle",
                InformationUrl = baseUrl + "/phone/about"
            }
        };
        phone.ConfigHash = PhoneConfigGenerator.Hash(PhoneConfigGenerator.Generate(mac, phone.Configuration));
        db.Phones.Add(phone);
        if (candidate is not null)
        {
            db.Discovered.Remove(candidate);
        }
        AddEvent(db, mac, "CONFIG", "Created configuration version 1; waiting for phone request.");
        await db.SaveChangesAsync();
        return phone;
    });

    public Task<Phone> Save(string mac, PhoneConfiguration config) => Use(async db =>
    {
        mac = Mac.Normalize(mac);
        var xml = PhoneConfigGenerator.Generate(mac, config);
        var phone = await db.Phones.FindAsync(mac) ?? throw new KeyNotFoundException("Phone not found.");
        db.Entry(phone.Configuration).CurrentValues.SetValues(config);
        phone.ConfigVersion++;
        phone.ConfigHash = PhoneConfigGenerator.Hash(xml);
        AddEvent(db, mac, "CONFIG", $"Saved configuration version {phone.ConfigVersion}; not yet served.");
        await db.SaveChangesAsync();
        return phone;
    });

    public Task<bool> Delete(string mac, bool discovered = false) => Use(async db =>
    {
        mac = Mac.Normalize(mac);
        if (discovered)
        {
            return await db.Discovered.Where(p => p.MacAddress == mac).ExecuteDeleteAsync() > 0;
        }
        var phone = await db.Phones.FindAsync(mac);
        if (phone is null) return false;
        db.Phones.Remove(phone);
        await db.SaveChangesAsync();
        return true;
    });

    public Task<long> RecordRequest(string filename, string ip) => Use(async db =>
    {
        var mac = Mac.FromFilename(filename);
        var now = DateTime.UtcNow;
        var entry = new TftpEvent { MacAddress = mac, SourceIp = ip, Filename = filename };
        db.TftpEvents.Add(entry);
        if (mac is not null)
        {
            var phone = await db.Phones.FindAsync(mac);
            if (phone is null)
            {
                var candidate = await db.Discovered.FindAsync(mac);
                if (candidate is null)
                {
                    candidate = new DiscoveryCandidate { MacAddress = mac, FirstSeenUtc = now };
                    db.Discovered.Add(candidate);
                }
                candidate.LastKnownIp = ip;
                candidate.LastSeenUtc = now;
                candidate.LastRequestedFile = filename;
                candidate.RequestCount++;
            }
            else
            {
                if (phone.LastKnownIp != ip)
                {
                    AddEvent(db, mac, "IP", $"IP changed {phone.LastKnownIp ?? "unknown"} → {ip}");
                }
                phone.LastKnownIp = ip;
                phone.FirstSeenUtc ??= now;
                phone.LastSeenUtc = now;
                phone.LastTftpUtc = now;
                if (filename.StartsWith("SEP", StringComparison.OrdinalIgnoreCase)) phone.LastConfigRequested = filename;
                AddEvent(db, mac, "TFTP", "Requested " + filename);
            }
        }
        await db.SaveChangesAsync();
        return entry.Id;
    });

    public Task<bool> FinishTransfer(long id, string result, long bytes, string? mac, int? version) => Use(async db =>
    {
        var entry = await db.TftpEvents.FindAsync(id);
        if (entry is not null)
        {
            entry.Result = result;
            entry.BytesTransferred = bytes;
        }
        if (result == "Completed" && mac is not null && version.HasValue)
        {
            var phone = await db.Phones.FindAsync(mac);
            if (phone is not null)
            {
                phone.LastServedConfigVersion = version;
                phone.LastConfigServedUtc = DateTime.UtcNow;
                AddEvent(db, mac, "CONFIG", $"Served configuration version {version} (final block acknowledged).");
            }
        }
        await db.SaveChangesAsync();
        return true;
    });

    public static void AddEvent(Database db, string mac, string type, string message)
    {
        db.Events.Add(new PhoneEvent { MacAddress = mac, Type = type, Message = message });
    }
}
