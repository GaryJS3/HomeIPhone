using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using HomeIPhone;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeIPhone.Tests;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "homeiphone-tests-" + Guid.NewGuid());
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PhoneServer:DataPath"] = DataPath,
            ["PhoneServer:TftpBindAddress"] = "127.0.0.1",
            ["PhoneServer:TftpPort"] = "0",
            ["PhoneServer:BaseUrl"] = "http://192.168.1.10:8080",
            ["Logging:LogLevel:Default"] = "Warning"
        }));
    }
}

public sealed class CoreTests
{
    [Theory]
    [InlineData("00:11:22:aa:bb:cc")]
    [InlineData("00-11-22-aa-bb-cc")]
    [InlineData("0011.22aa.bbcc")]
    [InlineData("001122aabbcc")]
    public void Normalize(string value) => Assert.Equal("001122AABBCC", Mac.Normalize(value));

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("00112233445")]
    [InlineData("00:11-22:33:44:55")]
    [InlineData("0011223344GG")]
    public void InvalidMac(string value) => Assert.Throws<ArgumentException>(() => Mac.Normalize(value));

    [Theory]
    [InlineData("SEP001122334455.cnf.xml")]
    [InlineData("SEP001122334455.cnf.xml.sgn")]
    [InlineData("SEP001122334455.cnf.xml.enc.sgn")]
    [InlineData("ITLSEP001122334455.tlv")]
    [InlineData("CTLSEP001122334455.tlv")]
    public void RecognizesDiscovery(string file) => Assert.Equal("001122334455", Mac.FromFilename(file));

    [Theory]
    [InlineData("XMLDefault.cnf.xml")]
    [InlineData("SEPDefault.cnf.xml")]
    [InlineData("term45.default.loads")]
    public void GenericNotDiscovered(string file) => Assert.Null(Mac.FromFilename(file));

    [Theory]
    [InlineData("../secret")]
    [InlineData("/etc/passwd")]
    [InlineData("%2e%2e%2fsecret")]
    [InlineData("C:\\secret")]
    [InlineData("foo/../../secret")]
    public void RejectsTraversal(string file) => Assert.False(TftpServerService.SafeFilename(file));

    [Fact]
    public void GeneratesValidEscapedXml()
    {
        var xml = PhoneConfigGenerator.Generate("001122334455", new() { ServicesUrl = "http://192.168.1.10/services?a=1&b=2", Description = "Kitchen & Hall" });
        var doc = PhoneConfigGenerator.Parse(xml);
        Assert.Equal("SEP001122334455", doc.Root!.Element("deviceName")!.Value);
        Assert.Equal("http://192.168.1.10/services?a=1&b=2", doc.Root.Element("servicesURL")!.Value);
        Assert.Null(doc.Root.Element("loadInformation"));
        Assert.Equal("0", doc.Root.Element("vendorConfig")!.Element("webAccess")!.Value);
        Assert.Equal("1", doc.Root.Element("vendorConfig")!.Element("sshAccess")!.Value);
    }

    [Theory]
    [InlineData("<broken>")]
    [InlineData("<!DOCTYPE device [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><device>&x;</device>")]
    public void RejectsUnsafeXml(string xml) => Assert.ThrowsAny<Exception>(() => PhoneConfigGenerator.Generate("001122334455", new() { RawOverrideXml = xml }));

    [Theory]
    [InlineData("DeviceInformationX")]
    [InlineData("NetworkConfigurationX")]
    [InlineData("EthernetInformationX")]
    [InlineData("PortInformationX")]
    [InlineData("DeviceLogX")]
    [InlineData("StreamingStatisticsX")]
    public void RetainsKnownUnknownAndRepeatedFields(string endpoint)
    {
        var fields = CiscoXmlParser.Flatten(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", endpoint + ".xml")));
        Assert.Contains(fields, p => p.Key.EndsWith("/Known") && p.Value == "value");
        Assert.Contains(fields, p => p.Key.EndsWith("/FutureField") && p.Value == "retained");
        Assert.Contains(fields, p => p.Key.EndsWith("/Counter[2]") && p.Value == "2");
        Assert.Contains(fields, p => p.Key.EndsWith("/@revision") && p.Value == "1");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("http://10.0.0.1")]
    [InlineData("example.com")]
    [InlineData("8.8.8.8")]
    [InlineData("::1")]
    public void RejectsUnsafePollingTargets(string ip) => Assert.False(CiscoPhoneHttpClient.AllowedIp(ip));
}

public sealed class IntegrationTests
{
    private static async Task<byte[]> Download(int port, string filename, bool dropFirstAck = false)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var rrq = new byte[] { 0, 1 }.Concat(Encoding.ASCII.GetBytes(filename + "\0octet\0")).ToArray();
        await udp.SendAsync(rrq, new IPEndPoint(IPAddress.Loopback, port), timeout.Token);
        using var result = new MemoryStream();
        ushort block = 1;
        while (true)
        {
            var packet = await udp.ReceiveAsync(timeout.Token);
            Assert.NotEqual(port, packet.RemoteEndPoint.Port);
            if (packet.Buffer[1] == 5) return packet.Buffer;
            Assert.Equal(3, packet.Buffer[1]);
            Assert.Equal(block, BinaryPrimitives.ReadUInt16BigEndian(packet.Buffer.AsSpan(2)));
            if (dropFirstAck)
            {
                var retry = await udp.ReceiveAsync(timeout.Token);
                Assert.Equal(packet.Buffer, retry.Buffer);
                dropFirstAck = false;
            }
            result.Write(packet.Buffer, 4, packet.Buffer.Length - 4);
            await udp.SendAsync(new byte[] { 0, 4, packet.Buffer[2], packet.Buffer[3] }, packet.RemoteEndPoint, timeout.Token);
            if (packet.Buffer.Length < 516) return result.ToArray();
            block++;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(1600)]
    public async Task StaticTransfersExactContentsAndRetry(int length)
    {
        await using var app = new AppFactory();
        using var http = app.CreateClient();
        var expected = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(app.DataPath, "tftp", "test.bin"), expected);
        var port = app.Services.GetRequiredService<TftpServerService>().BoundPort;
        Assert.Equal(expected, await Download(port, "test.bin", true));
        var missing = await Download(port, "missing.bin");
        Assert.Equal(5, missing[1]); Assert.Equal(1, missing[3]);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task DiscoveryAdoptServeSaveAndPersist()
    {
        await using var app = new AppFactory();
        using var http = app.CreateClient();
        var port = app.Services.GetRequiredService<TftpServerService>().BoundPort;
        var missing = await Download(port, "SEP001122334455.cnf.xml");
        Assert.Equal(1, missing[3]);
        var candidates = await http.GetFromJsonAsync<List<DiscoveryCandidate>>("/api/discovered");
        Assert.Single(candidates!);
        Assert.Equal("127.0.0.1", candidates![0].LastKnownIp);
        Assert.Equal(1, candidates[0].RequestCount);
        var adopt = await http.PostAsJsonAsync("/api/discovered/001122334455/adopt", new { friendlyName = "Kitchen" });
        adopt.EnsureSuccessStatusCode();
        Assert.Empty((await http.GetFromJsonAsync<List<DiscoveryCandidate>>("/api/discovered"))!);
        var xml = Encoding.UTF8.GetString(await Download(port, "SEP001122334455.cnf.xml"));
        Assert.Contains("SEP001122334455", xml);
        Assert.Contains("http://192.168.1.10:8080/phone/services", xml);
        var service = app.Services.GetRequiredService<PhoneService>();
        Phone phone = null!;
        for (var i = 0; i < 30; i++)
        {
            phone = (await service.Get("001122334455"))!;
            if (phone.LastServedConfigVersion == 1) break;
            await Task.Delay(25);
        }
        Assert.Equal(1, phone.LastServedConfigVersion);
        var previousHash = phone.ConfigHash;
        phone.Configuration.Description = "Changed";
        var save = await http.PutAsJsonAsync("/api/phones/001122334455/config", phone.Configuration);
        save.EnsureSuccessStatusCode();
        phone = (await service.Get(phone.MacAddress))!;
        Assert.Equal(2, phone.ConfigVersion);
        Assert.Equal(1, phone.LastServedConfigVersion);
        Assert.NotEqual(previousHash, phone.ConfigHash);
        Assert.Contains("Changed", await http.GetStringAsync("/api/phones/001122334455/config/preview"));
        Assert.Contains("Changed", Encoding.UTF8.GetString(await Download(port, "SEP001122334455.cnf.xml")));
        phone.Configuration.RawOverrideXml = "<bad>";
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync("/api/phones/001122334455/config", phone.Configuration)).StatusCode);
        Assert.Equal(2, (await service.Get(phone.MacAddress))!.ConfigVersion);
        // Independent context proves durable SQLite state rather than an in-memory cache.
        await using var db = new Database(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Database>().UseSqliteForTest(app.DataPath));
        Assert.Equal("Kitchen", (await db.Phones.FindAsync("001122334455"))!.Configuration.FriendlyName);
        Assert.Contains("Phone controller", await http.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/_framework/blazor.web.js")).StatusCode);
        Assert.Contains("CiscoIPPhoneMenu", await http.GetStringAsync("/phone/services"));
    }

    [Fact]
    public async Task UnacknowledgedConfigNeverMarksVersionServed()
    {
        await using var app = new AppFactory();
        using var http = app.CreateClient();
        var service = app.Services.GetRequiredService<PhoneService>();
        var phone = await service.Add("020000000099", "Timeout test");
        phone.Configuration.RawOverrideXml = "<device><description>Test</description></device>";
        await service.Save(phone.MacAddress, phone.Configuration);
        var port = app.Services.GetRequiredService<TftpServerService>().BoundPort;
        using var udp = new UdpClient();
        await udp.SendAsync(new byte[] { 0, 1 }.Concat(Encoding.ASCII.GetBytes("SEP020000000099.cnf.xml\0octet\0")).ToArray(), new IPEndPoint(IPAddress.Loopback, port));
        using var timeout = new CancellationTokenSource(10000);
        var first = await udp.ReceiveAsync(timeout.Token);
        Assert.Equal(3, first.Buffer[1]);
        using var stranger = new UdpClient();
        await stranger.SendAsync(new byte[] { 0, 4, 0, 1 }, first.RemoteEndPoint);
        Assert.Equal(5, (await stranger.ReceiveAsync(timeout.Token)).Buffer[3]);
        // Leave every server retry unacknowledged and wait for the recorded failure.
        for (var i = 0; i < 70; i++)
        {
            if ((await service.Requests(phone.MacAddress)).Any(r => r.Result.StartsWith("Failed"))) break;
            await Task.Delay(100);
        }
        Assert.Null((await service.Get(phone.MacAddress))!.LastServedConfigVersion);
        Assert.Contains(await service.Requests(phone.MacAddress), r => r.Result.Contains("ACK timeout"));
    }
    [Fact]
    public async Task RejectsWritesAndTraversal()
    {
        await using var app = new AppFactory();
        using var http = app.CreateClient();
        var port = app.Services.GetRequiredService<TftpServerService>().BoundPort;
        Assert.Equal(4, (await Download(port, "../phones.db"))[3]);
        using var udp = new UdpClient();
        await udp.SendAsync(new byte[] { 0, 2, 97, 0, 111, 99, 116, 101, 116, 0 }, new IPEndPoint(IPAddress.Loopback, port));
        using var timeout = new CancellationTokenSource(3000);
        Assert.Equal(4, (await udp.ReceiveAsync(timeout.Token)).Buffer[3]);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/phones", new { macAddress = "bad" })).StatusCode);
    }
}

internal static class TestOptions
{
    public static Microsoft.EntityFrameworkCore.DbContextOptions<Database> UseSqliteForTest(this Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Database> builder, string path)
        => Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite(builder, "Data Source=" + Path.Combine(path, "phones.db")).Options;
}
