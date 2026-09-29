using System.Net;
using System.Text;
using HomeIPhone;
namespace HomeIPhone.Tests;

public sealed class PollingTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task PartialTelemetryRetainsFieldsAndReportsUnsupportedEndpoints()
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath == "/DeviceInformationX"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<DeviceInformation><appLoadID>test-load</appLoadID><serialNumber>TEST</serialNumber><future>new</future></DeviceInformation>") }
            : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var result = await new CiscoPhoneHttpClient(http).Poll("192.168.1.2", default);
        Assert.Equal(14, result.Count);
        Assert.True(result["DeviceInformationX"].Success);
        Assert.Contains(result["DeviceInformationX"].Fields, p => p.Value == "new");
        Assert.False(result["StreamingStatisticsX?5"].Success);
    }

    [Theory]
    [InlineData("<not-xml")]
    [InlineData("<html><br></html>")]
    public async Task MalformedXmlDoesNotAbortSweep(string body)
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var result = await new CiscoPhoneHttpClient(http).Poll("10.0.0.2", default);
        Assert.All(result.Values, endpoint => Assert.False(endpoint.Success));
    }

    [Fact]
    public async Task OversizedResponseIsBounded()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 131073)) }));
        var result = await new CiscoPhoneHttpClient(http).Poll("10.0.0.2", default);
        Assert.All(result.Values, endpoint => Assert.Contains("128 KiB", endpoint.Error));
    }
}
