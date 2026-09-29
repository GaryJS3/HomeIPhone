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
        var firmwareLoad = NormalizeFirmwareLoad(config.FirmwareLoad);
        if (config.FriendlyName.Length > 120 || config.Description.Length > 512 || config.Location.Length > 120 || firmwareLoad.Length > 128 || config.NtpServer.Length > 253 || config.TimeZone.Length > 128 || config.DateTemplate.Length > 32)
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
            new XElement("fullConfig", "true"),
            new XElement("deviceProtocol", config.DeviceProtocol),
            new XElement("deviceSecurityMode", 1),
            new XElement("encrConfig", "false"),
            new XElement("devicePool", new XElement("dateTimeSetting",
                new XElement("dateTemplate", config.DateTemplate),
                new XElement("timeZone", config.TimeZone),
                new XElement("ntps", new XElement("ntp", new XAttribute("priority", 0), new XElement("name", config.NtpServer), new XElement("ntpMode", "unicast")))),
                new XElement("callManagerGroup", new XElement("members"))),
            // The 9.3 SIP firmware dereferences common/SIP profile objects while applying
            // a full config. A well-formed XML fragment is not a complete phone profile.
            new XElement("commonProfile", new XElement("phonePassword", ""),
                new XElement("backgroundImageAccess", "true"), new XElement("callLogBlfEnabled", 0)),
            new XElement("deviceName", "SEP" + Mac.Normalize(mac)),
            new XElement("description", config.Description),
            new XElement("servicesURL", config.ServicesUrl),
            new XElement("directoryURL", config.DirectoryUrl),
            new XElement("idleURL", config.IdleUrl),
            new XElement("idleTimeout", string.IsNullOrWhiteSpace(config.IdleUrl) ? 0 : 30),
            new XElement("informationURL", config.InformationUrl),
            new XElement("messagesURL", config.MessagesUrl),
            new XElement("vendorConfig", new XElement("webAccess", config.WebAccess ? 0 : 1), new XElement("sshAccess", config.SshAccess ? 0 : 1)),
            new XElement("transportLayerProtocol", 2));
        foreach (var name in new[] { "authenticationURL", "proxyServerURL", "secureAuthenticationURL", "secureServicesURL", "secureDirectoryURL", "secureIdleURL", "secureInformationURL", "secureMessagesURL" })
            root.Add(new XElement(name, ""));
        if (config.DeviceProtocol == "SIP")
        {
            root.Element("commonProfile")!.AddBeforeSelf(new XElement("sipProfile",
                new XElement("sipProxies", new XElement("registerWithProxy", "false")),
                new XElement("sipCallFeatures", new XElement("cnfJoinEnabled", "false")),
                new XElement("sipStack", new XElement("sipInviteRetx", 6), new XElement("sipRetx", 10),
                    new XElement("timerInviteExpires", 180), new XElement("timerRegisterExpires", 3600),
                    new XElement("timerRegisterDelta", 5), new XElement("timerKeepAliveExpires", 120),
                    new XElement("timerSubscribeExpires", 120), new XElement("timerSubscribeDelta", 5),
                    new XElement("timerT1", 500), new XElement("timerT2", 4000),
                    new XElement("maxRedirects", 70), new XElement("remotePartyID", "false"), new XElement("userInfo", "None")),
                new XElement("phoneLabel", string.IsNullOrWhiteSpace(config.FriendlyName) ? "HomeIPhone" : config.FriendlyName),
                new XElement("sipLines"), new XElement("voipControlPort", 5060),
                new XElement("startMediaPort", 16384), new XElement("stopMediaPort", 32766),
                new XElement("natEnabled", "false"), new XElement("natAddress", ""),
                new XElement("dialTemplate", ""), new XElement("softKeyFile", "")));
        }
        if (!string.IsNullOrWhiteSpace(firmwareLoad))
        {
            root.Add(new XElement("loadInformation", firmwareLoad));
        }
        return new XDocument(root).ToString();
    }

    private static string NormalizeFirmwareLoad(string firmwareLoad) => firmwareLoad.EndsWith(".loads", StringComparison.OrdinalIgnoreCase)
        ? firmwareLoad[..^6]
        : firmwareLoad;

    public static string Hash(string xml) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
}
