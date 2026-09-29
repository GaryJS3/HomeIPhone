using HomeIPhone;
using HomeIPhone.Components;
using Microsoft.EntityFrameworkCore;

if (args.Contains("--healthcheck"))
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        return (await http.GetAsync("http://127.0.0.1:8080/health")).IsSuccessStatusCode ? 0 : 1;
    }
    catch { return 1; }
}
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? "http://0.0.0.0:8080");
builder.Services.AddOptions<PhoneServerOptions>().BindConfiguration("PhoneServer")
    .Validate(o => o.PollIntervalSeconds >= 5 && o.HistoryRetentionHours >= 1 && o.TftpPort is >= 0 and <= 65535, "Invalid interval, retention or TFTP port")
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https", "BaseUrl must be an absolute HTTP URL")
    .ValidateOnStart();
builder.Services.AddDbContextFactory<Database>((services, options) =>
{
    var path = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PhoneServerOptions>>().Value.DataPath;
    Directory.CreateDirectory(Path.Combine(path, "tftp"));
    options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(path, "phones.db") }.ToString());
});
builder.Services.AddSingleton<DatabaseGate>();
builder.Services.AddSingleton<PhoneService>();
builder.Services.AddSingleton<TftpFileService>();
builder.Services.AddSingleton<TftpServerService>();
builder.Services.AddHostedService(p => p.GetRequiredService<TftpServerService>());
builder.Services.AddHttpClient<CiscoPhoneHttpClient>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
builder.Services.AddSingleton<PhonePoller>();
builder.Services.AddHostedService<PhonePollingService>();
builder.Services.AddHostedService<HistoryCleanupService>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync();
    await db.Database.MigrateAsync();
}
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception ex) when (ex is ArgumentException or System.Xml.XmlException or KeyNotFoundException or InvalidOperationException)
    {
        context.Response.StatusCode = ex is KeyNotFoundException ? 404 : ex is InvalidOperationException ? 409 : 400;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});
app.UseStaticFiles();
app.MapStaticAssets();
app.UseAntiforgery();
app.MapGet("/health", async (IDbContextFactory<Database> factory, TftpServerService tftp) =>
{
    await using var db = await factory.CreateDbContextAsync();
    var accessible = await db.Database.CanConnectAsync();
    return Results.Json(new { status = accessible && tftp.Bound ? "healthy" : "unhealthy", database = accessible, tftp = tftp.Bound }, statusCode: accessible && tftp.Bound ? 200 : 503);
});
app.MapGet("/api/phones", (PhoneService p) => p.List());
app.MapPost("/api/phones", async (AddPhone request, PhoneService p) => { var phone = await p.Add(request.MacAddress, request.FriendlyName); return Results.Created("/api/phones/" + phone.MacAddress, phone); });
app.MapGet("/api/phones/{mac}", async (string mac, PhoneService p) => await p.Get(mac) is { } phone ? Results.Ok(phone) : Results.NotFound());
app.MapDelete("/api/phones/{mac}", async (string mac, PhoneService p) => await p.Delete(mac) ? Results.NoContent() : Results.NotFound());
app.MapGet("/api/phones/{mac}/config", async (string mac, PhoneService p) => await p.Get(mac) is { } phone ? Results.Ok(phone.Configuration) : Results.NotFound());
app.MapPut("/api/phones/{mac}/config", (string mac, PhoneConfiguration config, PhoneService p) => p.Save(mac, config));
app.MapGet("/api/phones/{mac}/config/preview", async (string mac, PhoneService p) => await p.Get(mac) is { } phone ? Results.Text(PhoneConfigGenerator.Generate(mac, phone.Configuration), "application/xml") : Results.NotFound());
app.MapPost("/api/phones/{mac}/poll", async (string mac, PhonePoller p, CancellationToken token) => { await p.Poll(mac, token); return Results.Ok(); });
app.MapGet("/api/phones/{mac}/events", (string mac, PhoneService p) => p.Events(mac));
app.MapGet("/api/tftp", (string? mac, PhoneService p) => p.Requests(mac is null ? null : Mac.Normalize(mac)));
app.MapGet("/api/tftp/files", (TftpFileService files) => files.List());
app.MapPost("/api/tftp/files/{filename}", async (string filename, HttpRequest request, TftpFileService files, CancellationToken token) => Results.Ok(await files.ImportAsync(filename, request.Body, request.ContentLength, token)));
app.MapDelete("/api/tftp/files/{filename}", (string filename, TftpFileService files) => files.Delete(filename) ? Results.NoContent() : Results.NotFound());
app.MapGet("/api/discovered", (PhoneService p) => p.Discoveries());
app.MapPost("/api/discovered/{mac}/adopt", async (string mac, AddPhone request, PhoneService p) => Results.Ok(await p.Add(mac, request.FriendlyName, true)));
app.MapDelete("/api/discovered/{mac}", async (string mac, PhoneService p) => await p.Delete(mac, true) ? Results.NoContent() : Results.NotFound());
app.MapPhoneApplications();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
return 0;

public sealed record AddPhone(string MacAddress = "", string? FriendlyName = null);
public partial class Program;
