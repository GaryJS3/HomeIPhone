using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace HomeIPhone;

public static class PhoneConfigGenerator
{
    public static XDocument Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 262144
        });
        return XDocument.Load(reader);
    }

    public static string Generate(string mac, PhoneConfiguration config)
    {
        Mac.Normalize(mac);
        if (typeof(PhoneConfiguration).GetProperties().Where(p => p.PropertyType == typeof(string)).Any(p => p.GetValue(config) is null))
        {
            throw new ArgumentException("Configuration strings cannot be null; use an empty string for optional fields.");
        }
        foreach (var url in new[] { config.ServicesUrl, config.DirectoryUrl, config.IdleUrl, config.InformationUrl, config.MessagesUrl })
        {
            if (url.Length > 2048 || (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))))
            {
                throw new ArgumentException("Phone URLs must be absolute HTTP or HTTPS URLs.");
            }
        }
        if (config.FriendlyName.Length > 120 || config.Description.Length > 512 || config.Location.Length > 120 || config.FirmwareLoad.Length > 128 || config.NtpServer.Length > 253 || config.TimeZone.Length > 128 || config.DateTemplate.Length > 32)
        {
            throw new ArgumentException("One or more configuration fields exceed their size limit.");
        }
        if (config.Model is not ("Unknown" or "Cisco 7945G" or "Cisco 7965G"))
        {
            throw new ArgumentException("Unsupported model.");
        }
        if (config.DeviceProtocol is not ("SCCP" or "SIP"))
        {
            throw new ArgumentException("Device protocol must be SCCP or SIP.");
        }
        if (!string.IsNullOrWhiteSpace(config.RawOverrideXml))
        {
            var raw = Parse(config.RawOverrideXml);
            if (raw.Root?.Name != "device")
            {
                throw new ArgumentException("Raw configuration must have a device root element.");
            }
            return raw.ToString();
        }
        var root = new XElement("device",
            new XAttribute("{http://www.w3.org/2001/XMLSchema-instance}type", "axl:XIPPhone"),
            new XElement("fullConfig", "true"),
            new XElement("deviceProtocol", config.DeviceProtocol),
            new XElement("devicePool", new XElement("dateTimeSetting",
                new XElement("dateTemplate", config.DateTemplate),
                new XElement("timeZone", config.TimeZone),
                new XElement("ntps", new XElement("ntp", new XElement("name", config.NtpServer), new XElement("ntpMode", "Unicast"))))),
            new XElement("deviceName", "SEP" + Mac.Normalize(mac)),
            new XElement("description", config.Description),
            new XElement("servicesURL", config.ServicesUrl),
            new XElement("directoryURL", config.DirectoryUrl),
            new XElement("idleURL", config.IdleUrl),
            new XElement("informationURL", config.InformationUrl),
            new XElement("messagesURL", config.MessagesUrl),
            new XElement("vendorConfig", new XElement("webAccess", config.WebAccess ? 0 : 1), new XElement("sshAccess", config.SshAccess ? 0 : 1)),
            new XElement("transportLayerProtocol", 2));
        if (!string.IsNullOrWhiteSpace(config.FirmwareLoad))
        {
            root.Add(new XElement("loadInformation", config.FirmwareLoad));
        }
        return new XDocument(root).ToString();
    }

    public static string Hash(string xml) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
}
