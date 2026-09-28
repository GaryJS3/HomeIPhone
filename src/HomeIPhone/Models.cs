using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace HomeIPhone;

public sealed class PhoneServerOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public string TftpBindAddress { get; set; } = "0.0.0.0";
    public int TftpPort { get; set; } = 69;
    public int PollIntervalSeconds { get; set; } = 30;
    public int HistoryRetentionHours { get; set; } = 72;
    public string DataPath { get; set; } = "/data";
}

public static partial class Mac
{
    public static string Normalize(string value)
    {
        if (!Format().IsMatch(value ?? ""))
        {
            throw new ArgumentException("Enter a valid 12-digit MAC address.");
        }
        return Regex.Replace(value!, "[:.\\-]", "").ToUpperInvariant();
    }

    public static string? FromFilename(string filename)
    {
        var match = Filename().Match(filename);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"^(?:[0-9a-f]{12}|(?:[0-9a-f]{2}:){5}[0-9a-f]{2}|(?:[0-9a-f]{2}-){5}[0-9a-f]{2}|(?:[0-9a-f]{4}\.){2}[0-9a-f]{4})$", RegexOptions.IgnoreCase)]
    private static partial Regex Format();
    [GeneratedRegex(@"^(?:SEP|CTLSEP|ITLSEP)([0-9a-f]{12})(?:\.cnf\.xml(?:\.sgn|\.enc\.sgn)?|\.tlv)$", RegexOptions.IgnoreCase)]
    private static partial Regex Filename();
}

public sealed class Phone
{
    [Key] public string MacAddress { get; set; } = "";
    public PhoneConfiguration Configuration { get; set; } = new();
    public string? LastKnownIp { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FirstSeenUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public DateTime? LastHttpSuccessUtc { get; set; }
    public DateTime? LastHttpFailureUtc { get; set; }
    public DateTime? LastTftpUtc { get; set; }
    public string? LastConfigRequested { get; set; }
    public bool Online { get; set; }
    public string? Firmware { get; set; }
    public int ConfigVersion { get; set; } = 1;
    public string ConfigHash { get; set; } = "";
    public int? LastServedConfigVersion { get; set; }
    public DateTime? LastConfigServedUtc { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTime? SnapshotUtc { get; set; }
}

public sealed class PhoneConfiguration
{
    public string FriendlyName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Location { get; set; } = "";
    public string Model { get; set; } = "Unknown";
    public string ServicesUrl { get; set; } = "";
    public string DirectoryUrl { get; set; } = "";
    public string IdleUrl { get; set; } = "";
    public string InformationUrl { get; set; } = "";
    public string MessagesUrl { get; set; } = "";
    public string NtpServer { get; set; } = "pool.ntp.org";
    public string TimeZone { get; set; } = "Eastern Standard/Daylight Time";
    public string DateTemplate { get; set; } = "M/D/Ya";
    public bool WebAccess { get; set; } = true;
    public bool SshAccess { get; set; }
    public string FirmwareLoad { get; set; } = "";
    public string RawOverrideXml { get; set; } = "";
}

public sealed class DiscoveryCandidate
{
    [Key] public string MacAddress { get; set; } = "";
    public string LastKnownIp { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string LastRequestedFile { get; set; } = "";
    public long RequestCount { get; set; }
}

public sealed class TftpEvent
{
    public long Id { get; set; }
    public string? MacAddress { get; set; }
    public string SourceIp { get; set; } = "";
    public string Filename { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Result { get; set; } = "Requested";
    public long BytesTransferred { get; set; }
}

public sealed class PhoneEvent
{
    public long Id { get; set; }
    public string MacAddress { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}
