using System.Xml.Linq;
using Microsoft.Extensions.Options;
namespace HomeIPhone;

public static class PhoneApplications
{
    public static void MapPhoneApplications(this WebApplication app)
    {
        app.MapGet("/phone/services", (IOptions<PhoneServerOptions> options) =>
        {
            var root = new XElement("CiscoIPPhoneMenu", new XElement("Title", "HomeIPhone"), new XElement("Prompt", "Phone controller"));
            foreach (var item in new[] { ("Device Status", "status"), ("Server Status", "server"), ("About", "about") })
                root.Add(new XElement("MenuItem", new XElement("Name", item.Item1), new XElement("URL", options.Value.BaseUrl.TrimEnd('/') + "/phone/" + item.Item2)));
            return Results.Text(root.ToString(), "text/xml");
        });
        app.MapGet("/phone/directory", () => Results.Text(new XElement("CiscoIPPhoneDirectory", new XElement("Title", "Directory"), new XElement("Prompt", "No calling configured")).ToString(), "text/xml"));
        foreach (var route in new[] { "idle", "status", "server", "about" })
        {
            app.MapGet("/phone/" + route, () => Results.Text(new XElement("CiscoIPPhoneText", new XElement("Title", "HomeIPhone"), new XElement("Prompt", "Provisioning controller"), new XElement("Text", "Connected to HomeIPhone. Manage this phone using the web dashboard. Calling is not implemented.")).ToString(), "text/xml"));
        }
    }
}
