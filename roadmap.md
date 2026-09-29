# HomeIPhone roadmap

Updated: 2026-09-29

## Current state

HomeIPhone is deployed as the `homeiphone` Dockhand Git stack on the Automation host at `10.44.0.33`. The live health endpoint is healthy, the controller is reachable on TCP/8080, and TFTP is bound on UDP/69 with host networking.

The controller supports phone discovery and adoption, persistent configuration, generated Cisco XML, polling and diagnostics, TFTP request history, firmware/support-file uploads, and automatic extraction of `.tar`, `.tar.gz`, and `.tgz` uploads. It is a provisioning and diagnostics controller, not a PBX; SIP/SCCP call control, registration, paging, intercom, and audio are outside the current scope.

The test phone is a Cisco CP-7965G, MAC `1C1D862F31AF`, at `10.44.65.175`. DHCP points its TFTP server at `10.44.0.33`. Its saved HomeIPhone profile is SIP with load `SIP45.9-3-1SR4-1S`, configuration version 9, and no raw XML override. On 2026-09-29 it applied the generated profile, displayed the configured name and HomeIPhone idle message, and reported the expected Eastern timezone and current time after syncing from NTP.

The phone successfully converted from SCCP to SIP on 2026-09-29. The decisive live sequence was:

1. `term65.default.loads` completed.
2. `jar45sip.9-3-1ES26.sbn` completed.
3. `cnu45.9-3-1ES26.sbn` completed.
4. `apps45.9-3-1ES26.sbn` completed.
5. `dsp45.9-3-1ES26.sbn` completed.
6. `cvm45sip.9-3-1ES26.sbn` completed.
7. The phone rebooted, fetched `SEP1C1D862F31AF.cnf.xml`, came back online, and telemetry reported `jar45sip.9-3-1ES26.sbn`.

After SIP conversion, the initial minimal XML downloaded but was rejected during application. The 9.3.1 firmware log exposed required profile structure (at least one Call Manager member and one SIP line); the phone then accepted version 9. It also logged `Local clock reset to NTP reference.` The SIP profile includes a placeholder Call Manager address and does not register or provide calling. See the detailed phone acceptance notes in `testing-and-environment.md`.

The failed 9.2.1 attempt is closed. The phone repeatedly downloaded `term65.default.loads` without requesting an image. The 9.2.1 files and temporary `XMLDefault.cnf.xml` were removed from the runtime TFTP directory. The runtime now contains only the successful SIP 9.3.1 SR4-1 package. Licensed firmware remains runtime data and is excluded from Git.

## Completed

- GitHub/Dockhand deployment through the committed Linux x64 `publish/` output.
- Persistent SQLite inventory, configuration versions, hashes, discovery, polling snapshots, and event history.
- Cisco 7945G/7965G generated profiles with SCCP/SIP selection.
- Safe firmware uploads and automatic archive extraction with path, symlink, size, and entry-count limits.
- Firmware load normalization so an optional `.loads` suffix is removed from generated XML.
- TFTP request tracking with source IP, result, byte count, configuration version, and final-ACK semantics.
- Live physical-phone conversion acceptance using the matching SIP 9.3.1 package.
- Live generated SIP profile acceptance, Eastern timezone/NTP synchronization, and HomeIPhone idle message on a CP-7965G.
- Controller deployment verified healthy on Automation after the profile change.

## Next priorities

### 1. Preserve the successful firmware workflow

- Keep the current SIP 9.3.1 runtime package as the known-good acceptance baseline.
- Do not commit firmware, archives, manifests, or `XMLDefault.cnf.xml` into the repository.
- Before any future firmware change, record the phone's current application load and capture TFTP requests by source IP.
- Treat repeated `term65.default.loads` without `.sbn` requests as a failed load/authentication or compatibility test.

### 2. Improve firmware operations UX

- Show the current runtime firmware set and the most recent phone-specific firmware transfer sequence in the Firmware page.
- Add a compatibility note for 7945G/7965G model and firmware train before an operator starts a recovery upgrade.
- Make it obvious that a served configuration or manifest is not proof that the phone accepted it.

### 3. Add controlled phone actions

- Consider a guarded phone restart/reset action only after the physical-phone workflow and authorization model are defined.
- Keep firmware conversion separate from ordinary configuration refreshes; a normal reboot must not silently change firmware.

### 4. Expand acceptance coverage

- Validate a second 7945G/7965G device if available.
- Exercise trust-list clearing, signed/unsigned configuration behavior, DHCP Option 150 changes, and recovery from a missing file.
- Exercise Services-button navigation and status/about routes on the SIP image; idle message, directory URL configuration, and service URLs are verified, while every menu path remains to be checked on the display.

## Constraints and risks

- PhoneFirmware is contract-provided and must stay ignored by Git.
- The application has no authentication; keep HTTP and TFTP on the trusted phone/management network.
- Linux host networking is required because TFTP data transfers use ephemeral UDP ports.
- A healthy controller and a completed TFTP transfer do not prove that a phone accepted a configuration or firmware image; check phone telemetry and console logs.
- Firmware versions are Cisco-specific. Use a package matching the phone's current train or an explicitly tested intermediate path.
- Current physical acceptance covers one CP-7965G with SIP 9.3.1 SR4-1. It does not certify other models, firmware trains, SCCP behavior, signed/trust-list workflows, recovery mode, or calling.
