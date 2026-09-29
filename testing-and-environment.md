# Testing and environment

Updated: 2026-09-29

## Repository and development machine

- Repository: `https://github.com/GaryJS3/HomeIPhone.git`
- Branch: `main`
- Workspace: `C:\Users\gary\source\repos\HomeIPhone`
- Shell: PowerShell
- Language/runtime: C# and .NET 10
- Local contract firmware: `PhoneFirmware/` (ignored by Git)
- Local runtime test data: `data/` (ignored by Git)
- Previous application commit: `f7b6cc9` (`Normalize Cisco firmware load names`)

Do not add contract firmware or extracted `.sbn`/`.loads` files to Git. The Firmware page uploads them to the runtime volume instead.

## Automation host

- Host: `10.44.0.33`
- SSH user: `auto`
- Docker management: Dockhand Git stack `homeiphone`
- Web/TFTP controller: `http://10.44.0.33:8080`
- Health: `GET /health`
- TFTP: UDP/69 plus ephemeral transfer ports
- Persistent volume: `/data`
- TFTP directory: `/data/tftp`
- Container network: host mode
- Web listener: TCP/8080

The production stack mounts the committed `publish/` directory read-only and stores SQLite and runtime TFTP files in the named `homeiphone-data` volume. Do not use `docker compose down -v` on Automation.

## Current physical-phone acceptance target

- Model: Cisco CP-7965G
- MAC: `1C1D862F31AF`
- IP: `10.44.65.175`
- DHCP TFTP server: `10.44.0.33`
- Current application load: `jar45sip.9-3-1ES26.sbn`
- Current saved controller load: `SIP45.9-3-1SR4-1S`
- Protocol: SIP

The known-good runtime set is the SIP 9.3.1 SR4-1 package:

```text
apps45.9-3-1ES26.sbn
cnu45.9-3-1ES26.sbn
cvm45sip.9-3-1ES26.sbn
dsp45.9-3-1ES26.sbn
jar45sip.9-3-1ES26.sbn
SIP45.9-3-1SR4-1S.loads
term45.default.loads
term65.default.loads
```

## Local verification

Run focused checks after source changes:

```powershell
dotnet test -c Release --verbosity minimal
git diff --check
dotnet restore src/HomeIPhone/HomeIPhone.csproj -r linux-x64
dotnet publish src/HomeIPhone/HomeIPhone.csproj -c Release -r linux-x64 --self-contained false --no-restore -o publish /p:UseAppHost=false
```

The current test suite has 53 passing tests. Tests cover XML parsing, generated profiles, validation, persistence, discovery/adoption, TFTP transfers and retries, archive extraction, and API behavior. They do not prove hardware firmware acceptance.

## Deployment verification

After committing and pushing a source change:

1. Confirm the exact commit is on `origin/main`.
2. Confirm Dockhand reports the stack synchronized to that commit.
3. Confirm the container is running on Automation.
4. Check `http://10.44.0.33:8080/health` and require `status=healthy`, `database=true`, and `tftp=true`.
5. Verify the intended runtime files through `GET /api/tftp/files`.
6. Use a live phone or a controlled TFTP probe to validate the changed behavior.

Local success, a Git sync result, or a running container alone is not deployment proof.

## Phone provisioning test

1. Set DHCP Option 150 to `10.44.0.33` and verify the phone receives that value.
2. Confirm the phone appears under `/api/discovered` or is already adopted.
3. Confirm the saved profile has the intended model, protocol, and firmware load.
4. Preview `/api/phones/1C1D862F31AF/config/preview` and check the generated load value has no `.loads` suffix.
5. Reboot normally and inspect `/api/tftp?mac=1C1D862F31AF`.
6. Confirm `SEP1C1D862F31AF.cnf.xml` completes and the phone returns online.
7. Confirm telemetry through `/api/phones/1C1D862F31AF`.

For a firmware conversion, the acceptance sequence is a completed model manifest, every referenced `.sbn` file, a reboot, a new configuration fetch, and telemetry showing the new application load. A manifest alone is not success.

## Recovery firmware test

Use this only with a compatible licensed package already present in runtime TFTP:

1. Capture the current firmware and configuration version.
2. Place the phone in recovery mode using the documented physical procedure.
3. Watch TFTP events by the phone's source IP; ignore local test traffic from other addresses.
4. Confirm the manifest and every image complete.
5. Wait for the phone to reboot and return online.
6. Confirm the application load in `DeviceInformationX` telemetry.

Repeated `term65.default.loads` with no `.sbn` requests indicates a rejected or incompatible manifest. Stop changing configuration and obtain a package matching the phone's firmware train. The failed 9.2.1 attempt demonstrated this exact pattern; the matching 9.3.1 package completed successfully.

## Useful live checks

```powershell
Invoke-RestMethod http://10.44.0.33:8080/health
Invoke-RestMethod http://10.44.0.33:8080/api/tftp/files
Invoke-RestMethod http://10.44.0.33:8080/api/tftp?mac=1C1D862F31AF
Invoke-RestMethod http://10.44.0.33:8080/api/phones/1C1D862F31AF
```

To distinguish phone traffic from a workstation probe, inspect `sourceIp`. The test phone uses `10.44.65.175`; the development workstation used `10.0.0.232` during local TFTP probes.

## Known limitations

- No SIP/SCCP call control or registration service is implemented.
- No remote Call Manager restart/reset command is implemented.
- The controller does not generate ITL/CTL files or sign configurations.
- TFTP serves flat files only and ignores optional RRQ negotiation.
- HTTP polling depends on the phone's web access and private IPv4 reachability.
- Physical-phone testing remains required for firmware-specific behavior.
