# HomeIPhone

A single-container, .NET 10 / C# / Blazor Server controller for Cisco 7945G and 7965G provisioning and diagnostics. SQLite stores inventory, configuration, discovery and recent history. This is **not a PBX**: SIP/SCCP call control, calling, paging, intercom and audio are not implemented.

## Deploy with Docker / Dockhand

The root `compose.yaml` is the production example. On a Linux Docker host:

```sh
export PhoneServer__BaseUrl=http://YOUR_HOST_IP:8080
docker compose build
docker compose up -d
curl -f http://localhost:8080/health
```

For Dockhand, add this repository as a Git stack on the target environment, branch `main`, compose path `compose.yaml`, and enable automatic Git updates. The production Compose file pulls the official ASP.NET runtime image and mounts the committed `publish/` output read-only, so the Docker host does not need to build the SDK image. Set `PhoneServer__BaseUrl` in the stack environment to an address phones can reach. The included default targets Automation at `http://10.44.0.33:8080`; change it for other hosts. No registry image or extra service is needed.

Linux host networking is intentional: TFTP listens on UDP/69 and sends each transfer from an ephemeral UDP port. Open the appropriate stateful firewall path between the phone VLAN and host; publishing only UDP/69 through Docker bridge networking is insufficient. The container runs as root for reliable privileged-port binding and named-volume initialization. HTTP uses TCP/8080. Check for port conflicts before deploying. One replica only.

The named volume `homeiphone-data` mounts at `/data`: `phones.db`, SQLite journal files and optional `tftp/` files. Recreating the container retains inventory and configuration. Never use `docker compose down -v` unless you intend to erase data. Back up the volume with the app stopped, or use SQLite's online backup facility. EF migrations run at startup.

Use the **Firmware** page to upload licensed Cisco firmware, locale, ringtone, and support files. Uploads are stored in `/data/tftp`, are served as flat read-only TFTP files, and are not part of Git. `.tar`, `.tar.gz`, and `.tgz` uploads are extracted automatically; only regular files with safe flat names are imported, and the archive is discarded. The local `PhoneFirmware/` folder is ignored by the repository so contract-provided files are not redistributed.

| Environment variable | Default | Purpose |
| --- | --- | --- |
| `PhoneServer__BaseUrl` | `http://localhost:8080` in app; Automation IP in compose | Phone-facing absolute HTTP/HTTPS URL |
| `PhoneServer__TftpBindAddress` | `0.0.0.0` | UDP listener and transfer interface |
| `PhoneServer__TftpPort` | `69` | UDP listener port |
| `PhoneServer__PollIntervalSeconds` | `30` | Delay between polling sweeps; minimum 5 |
| `PhoneServer__HistoryRetentionHours` | `72` | Event retention; minimum 1 |
| `PhoneServer__DataPath` | `/data` | Persistent state directory |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` | Web listener; container health probe expects port 8080 |

`GET /health` checks database connectivity and the TFTP bind. It does not mean any physical phone has accepted a configuration.

## Bring a phone online

1. Configure **DHCP Option 150** with the Docker host IP, and renew/reboot the phone. HomeIPhone does not serve DHCP.
2. A request containing a Cisco MAC creates a **Discovered device** with source IP, first/last seen, request count and filename. Generic requests never create candidates. Nothing is automatically adopted.
3. Click **Quick Add**, choose a friendly name and save. Alternatively, **Add Phone** accepts colon, hyphen, dotted or plain 12-digit MAC formats.
4. The next `SEP<MAC>.cnf.xml` read dynamically renders the saved configuration. It takes precedence over a static file of the same name. Unknown files receive TFTP File Not Found.
5. Open the phone's **Status** tab. Polling uses the IP learned from TFTP; **Refresh Now** runs immediately. Enable the phone's embedded web access and permit HTTP/80 from the controller to it.
6. Edit **Config**, preview XML and save. Each successful save increments the version and hashes the final XML with SHA-256. Saved and last-served versions are separate. Served is recorded only after the final TFTP ACK; it does **not** prove the phone applied the settings.

The structured profile defaults to SCCP for the 7945G/7965G target and lets you select SIP when the phone has SIP firmware. It configures no call manager, account or registration, so calling remains out of scope. Firmware acceptance and required additional elements vary; use the full-file XML override for an existing known-good device profile. The override replaces all structured XML settings, is validated before saving, and can be cleared to restore generation. Blank firmware load omits `loadInformation`; no firmware is bundled or downloaded.

The `/phone/services`, `/phone/directory`, `/phone/idle`, `/phone/status`, `/phone/server` and `/phone/about` routes return simple Cisco XML placeholders. They intentionally provide no calling or home automation.

## Diagnostics and limitations

- Polls DeviceInformationX, NetworkConfigurationX, EthernetInformationX, PortInformationX?1–3, DeviceLogX?0–2 and StreamingStatisticsX?1–5. Unknown XML fields, attributes and repeated fields remain visible. Unsupported endpoints appear with individual errors.
- HTTP has a two-second timeout per endpoint, a 128 KiB response cap, no redirects, no proxy and no DNS targets. Only private IPv4 addresses learned by TFTP are polled. IPv6/public-address phones are currently unsupported.
- The latest complete poll replaces the previous snapshot. Retained event history includes TFTP, configuration saves/transfers, IP and firmware changes, reachability changes and changed endpoint errors. Identical polls/errors do not create repeated events. History is pruned every ten minutes; views return the newest 200 events/requests. Log XML is capped with other HTTP responses.
- The read-only TFTP server uses 512-byte octet transfers, per-transfer ports, five attempts with one-second ACK deadlines, and zero-length terminal blocks when needed. WRQ and netascii are rejected. Optional RRQ negotiation is ignored, so clients fall back to standard 512-byte transfers. At most 32 transfers run concurrently.
- Static TFTP files must be plain files directly inside `/data/tftp`. Subdirectories, path separators, percent-encoding and symlinks are deliberately rejected. This is narrower than a full firmware distribution server.
- Repeated ITL/CTL or signed/encrypted configuration requests produce a trust warning. The phone may reject unsigned files until previous CUCM trust state is cleared using the appropriate device procedure. HomeIPhone does not generate ITL/CTL files or sign configurations.
- If discovery stays empty, inspect DHCP Option 150, routed VLAN reachability, firewall/UDP transfer ports, a competing TFTP server, and System → TFTP bound status. If telemetry is unavailable, check phone web access, HTTP routing and endpoint errors.
- No physical 7945G/7965G has been exercised during development. Real boot, firmware-specific XML acceptance, Services-button navigation and ITL/CTL behavior require the hardware acceptance workflow. Synthetic tests do not certify those behaviors.

**Do not expose this application or TFTP service directly to the Internet.** There is no authentication. Any LAN client with access can edit inventory/configuration; keep it on a trusted management network. HTML is encoded, XML DTD/entity expansion is prohibited, and TFTP cannot write or read arbitrary filesystem paths.

Cisco references: [Remote monitoring guide](https://www.cisco.com/c/en/us/td/docs/voice_ip_comm/cuipph/7975G_7971g-ge_7970g_7965g_7945g/9_0/english/admin_guide/P747_BK_W06BD6D9_00_adminguide_7945-7965-7970-7971-7975/P747_BK_W06BD6D9_00_adminguide_7945-7965-7970-7971-7975_chapter_01000.html), [IP Phone Services developer guide](https://www.cisco.com/en/US/docs/voice_ip_comm/cuipph/all_models/xsi/9_1_1/xsidevguide911.pdf).

## API

- `GET/POST /api/phones` — create body: `{ "macAddress": "001122334455", "friendlyName": "Kitchen" }`
- `GET/DELETE /api/phones/{mac}`
- `GET/PUT /api/phones/{mac}/config` — PUT the configuration object returned by GET
- `GET /api/phones/{mac}/config/preview`
- `POST /api/phones/{mac}/poll`
- `GET /api/phones/{mac}/events`
- `GET /api/tftp?mac=001122334455` — omit MAC for all recent requests
- `GET /api/tftp/files`, `POST /api/tftp/files/{filename}`, `DELETE /api/tftp/files/{filename}` — manage static TFTP files; POST streams regular files to `/data/tftp` and automatically extracts `.tar`, `.tar.gz`, and `.tgz` archives
- `GET /api/discovered`
- `POST /api/discovered/{mac}/adopt` — body: `{ "friendlyName": "Kitchen" }`
- `DELETE /api/discovered/{mac}` — later requests can rediscover the phone

Invalid input returns 400, missing phone/candidate 404, duplicate MAC 409. Raw XML limit: 256 KiB.

## Develop / verify

Install the .NET 10 SDK to run tests or refresh the committed production publish output, then:

```sh
dotnet test
dotnet restore src/HomeIPhone/HomeIPhone.csproj -r linux-x64
dotnet publish src/HomeIPhone/HomeIPhone.csproj -c Release -r linux-x64 --self-contained false --no-restore -o publish /p:UseAppHost=false
dotnet run --project src/HomeIPhone --no-launch-profile -- --PhoneServer:DataPath=./data --PhoneServer:TftpPort=1069 --urls=http://localhost:8080
```

Tests exercise real UDP transfers, dropped ACK retry, final short/empty blocks, missing files, discovery/adoption, config versions/hashes, persistence through independent database contexts, validation, parser fixtures and API responses. Fixtures are synthetic representative XML, not captures from physical phones. Container validation additionally requires Linux Docker; Desktop networking is not a substitute for a physical-phone LAN test.

The repository includes the Release `publish/` output used by the Dockerfile. It is produced for the Linux x64 Automation host and intentionally excludes the .NET runtime. Refresh it after application changes before pushing; this keeps Dockhand’s small Automation host from needing a full SDK build during deployment. Regenerate the output for a different host architecture.
## Deployment validation notes

Automation's Hawser 0.2.46 runs Compose in a read-only systemd environment. A local `build:` fails with `mkdir /root/.docker: read-only file system`, so the production Compose file intentionally has no build step. A Git stack marked `synced` only verifies checkout, not a running or healthy container; check `/health` after deployment.
