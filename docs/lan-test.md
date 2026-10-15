# LAN setup and acceptance

Version 0.3.0 is a view-only LAN candidate. Milestone 3 remains pending until a session passes the checklist below on two authorized Windows computers. A local loopback test does not replace this check.

## Prepare both computers

1. Connect both Windows computers to the same trusted private network. Guest Wi-Fi/client isolation may prevent communication.
2. Use the same candidate build on both. The default package is `ProjectRemote-LAN-0.3.0-win-x64.zip` beside the project directory; use the successfully generated archive linked in the handoff. Extract it fully, then run `RemoteDesktop.App.exe`. This self-contained package needs no separate .NET runtime. If building from source, use the commands in [README](../README.md).
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
3. Ask the host to approve. Once approved, the selected screen should update. The viewer has no keyboard/mouse control.
4. Use **Disconnect / cancel** to stop. An ended session needs a fresh host session and fresh connection details.

Pairing details expire after five minutes; host approval times out after 30 seconds. A host allows one approved session, ending after at most one hour. Restarting hosting creates a new certificate fingerprint and pairing code. Five failed authentication/approval attempts end the listener. Do not reuse old details.

## Acceptance checklist

Record Windows versions, application version, connection type, selected source, approximate session duration and pass/fail for each item. Never include the pairing code or fingerprint in diagnostic reports/screenshots you share publicly.

- [ ] Two distinct computers connect using the host's private IPv4 address; TLS and explicit host approval succeed.
- [ ] A changing, non-sensitive test window is visible on the viewer; resize works and the displayed content is current.
- [ ] Declining approval sends no screen frames. Start a fresh session afterward.
- [ ] A wrong fingerprint fails. A wrong pairing code fails and does not prompt for approval.
- [ ] Waiting without approval times out. Cancel while connecting/awaiting approval releases the viewer.
- [ ] Host **Stop sharing** clears the viewer and ends the listener. Viewer **Disconnect** ends sharing on the host.
- [ ] Leaving Host/Connect or closing either application ends the active session.
- [ ] Disconnecting the network ends or times out the session; restoring it does not silently resume sharing.
- [ ] A new hosting session uses new details, and a new approved connection works.
- [ ] Whole-display capture works for the intended monitor; protected content may remain blank.

If connection fails, recheck the host's current IPv4 address, exact port, Private firewall permission and Wi-Fi isolation. An occupied port requires a new port on both sides. If details expire, start a new session. Keep screenshots focused on test content and report the visible status instead of sharing credentials.
