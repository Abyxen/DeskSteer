# LAN setup and acceptance

For current 0.4.0 source, both endpoints must use protocol v2. Default hosting remains viewing only; optional primary-display keyboard/mouse mode requires the separate [input checks](input-test.md). Historical packages below are local chat deliverables, not downloadable GitHub releases. Build a current candidate with the README commands or `package.ps1 -DestinationPath` and a new filename. No 0.4.0 ZIP is recorded yet.

DeskSteer 0.3.1 is the renamed view-only LAN candidate. The acceptance below belongs to its predecessor 0.3.0 build; it is not a new two-computer test of the branded package. **Milestone 3 accepted:** the user reported "All two-computer LAN checks passed" on two authorized Windows computers. This is user-reported acceptance; the agent did not inspect the session. Windows versions and session duration were not supplied. The checklist remains below for reproducing acceptance on other devices. The historical build had no remote input. Current 0.4.0 implements an input candidate, with Milestone 4 acceptance pending.

Current DeskSteer 0.3.1 archive: `DeskSteer-LAN-0.3.1-win-x64-candidate.zip`. SHA-256: `7c32293c65b9e43f9884b07c3cb1eba8519bc7205241ec4c3c9de261f3877915`. Package validation passed 78 tests (21 application, 7 core, 50 transport), with six native checks skipped; the separate native-enabled branding run passed all 84.

Historical accepted 0.3.0 archive: the accepted 0.3.0 candidate (original branding), 72,214,743 bytes. It retains its original branding. Historical SHA-256: `5eda557439d536951020334ca5a900cfd7b37716b0713abf2b6ec4355312128a`.

## Prepare both computers

1. Connect both Windows computers to the same trusted private network. Guest Wi-Fi/client isolation may prevent communication.
2. Use the same candidate build on both. The new package is `DeskSteer-LAN-0.3.1-win-x64-candidate.zip`, beside the project directory. Packaging completed; verify its SHA-256 against the current value above. The package validation run passed 78 tests, with six native checks skipped. Do not identify the historical 0.3.0 archive as this rebuild. Extract it fully, then run `DeskSteer.App.exe`. This self-contained package needs no separate .NET runtime. If building from source, use the commands in [README](../README.md).
3. On the host, open Windows Settings → Network & internet → your connection's properties. Find its IPv4 address, such as `192.168.1.25`. The address must begin with `10.`, `192.168.`, or `172.16.` through `172.31.`. The default `127.0.0.1` works only on the same computer.
4. If Windows Firewall prompts for access, allow the application on your trusted Private network. The app does not create firewall rules. If access remains blocked, allow this app on the Private profile through Windows Security → Firewall & network protection → Allow an app through firewall. Do not disable the firewall. No router port forwarding is needed.

## Host

1. Open **Host session**. Enter this computer's private IPv4 address. Keep port `47731`, or choose another unused port from 1 to 65535.
2. Choose **Choose screen and start hosting**. In the Windows picker, select a window/display you intend to share.
3. Send the shown address/port and pairing code privately to the intended viewer. Separately confirm the complete fingerprint through a trusted channel, such as a voice call. The code grants permission to request approval; it does not bypass host approval.
4. Wait for the request. The displayed viewer name is claimed by the viewer, not verified identity. Choose **Approve viewing** only for the intended request. No screen frames are sent before approval.
5. Keep the active sharing indicator visible. **Stop sharing** revokes the session and clears the connection details.

## Viewer

1. Open **Connect**. Enter the host's address, port, complete fingerprint, pairing code and your name.
2. Confirm the fingerprint with the host through the separate trusted channel, then choose **Request view**.
3. Ask the host to approve. Once approved, the selected screen should update. In the historical/default viewing flow the viewer has no keyboard/mouse control. Current 0.4.0 opt-in control requires separate host approval; see the input guide.
4. Use **Disconnect / cancel** to stop. An ended session needs a fresh host session and fresh connection details.

Pairing details expire after five minutes; host approval times out after 30 seconds. A host allows one approved session, ending after at most one hour. Restarting hosting creates a new certificate fingerprint and pairing code. Five failed authentication/approval attempts end the listener. Do not reuse old details.

## Acceptance checklist

All items below are marked passed based on the explicit user confirmation for 0.3. For subsequent runs, record Windows versions, application version, connection type, selected source, approximate session duration and pass/fail for each item. Never include the pairing code or fingerprint in diagnostic reports/screenshots you share publicly.

- [x] Two distinct computers connect using the host's private IPv4 address; TLS and explicit host approval succeed.
- [x] A changing, non-sensitive test window is visible on the viewer; resize works and the displayed content is current.
- [x] Declining approval sends no screen frames. Start a fresh session afterward.
- [x] A wrong fingerprint fails. A wrong pairing code fails and does not prompt for approval.
- [x] Waiting without approval times out. Cancel while connecting/awaiting approval releases the viewer.
- [x] Host **Stop sharing** clears the viewer and ends the listener. Viewer **Disconnect** ends sharing on the host.
- [x] Leaving Host/Connect or closing either application ends the active session.
- [x] Disconnecting the network ends or times out the session; restoring it does not silently resume sharing.
- [x] A new hosting session uses new details, and a new approved connection works.
- [x] Whole-display capture works for the intended monitor; protected content may remain blank.

If connection fails, recheck the host's current IPv4 address, exact port, Private firewall permission and Wi-Fi isolation. An occupied port requires a new port on both sides. If details expire, start a new session. Keep screenshots focused on test content and report the visible status instead of sharing credentials.
