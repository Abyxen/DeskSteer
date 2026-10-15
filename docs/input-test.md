# Keyboard and mouse acceptance

DeskSteer 0.4.0 implements an input candidate. **Milestone 4 is not accepted.** Recording backends test command construction/ownership without injecting real input; the steps below require two authorized Windows computers and actual keyboard/mouse behavior. Record OS versions, app version, display layout/scaling and pass/fail. Do not include pairing secrets in reports.

## Set up

1. Build the same 0.4.0 candidate on both computers, or use the supplied `DeskSteer-LAN-0.4.0-final.zip`. Complete the [LAN connection steps](lan-test.md), including separate fingerprint verification.
2. On the host, open a disposable Notepad document and a scrollable test window on the primary display. Run both apps normally, without administrator elevation. Clear unsaved personal work from the intended test area.
3. First leave **Share the primary display with optional keyboard and mouse control** unchecked. Start sharing through the Windows picker, approve viewing and verify input is unavailable. This mode remains viewing only.
4. Stop that session. Check the control option and start a new session. This shares the primary display directly instead of opening the picker. Approve viewing first; this approval alone grants no input authority.
5. On the viewer choose **Request keyboard and mouse**. Inspect the separate host prompt: its name is claimed by the viewer. Approve only the intended request. Confirm the host topmost active-control indicator is visible.

## Exercise control and release

Click the displayed screen on the viewer to focus it, then test the disposable host document. Keyboard input goes to the host foreground application, not a single chosen window. Mouse movement/clicks/wheel map to the host primary display only; black letterbox margins are outside the input region.

- [ ] Before viewing approval, no image/control is available. After viewing approval but before separate control approval, viewer input has no effect.
- [ ] Decline a control request and let another time out; neither grants input. Request again and approve explicitly.
- [ ] Type simple text, Backspace, Enter and arrow keys into host Notepad. Check Shift/Ctrl modifiers and ordinary shortcuts against disposable content.
- [ ] Move to image corners/center; left/right/middle clicks reach the intended primary-display positions. Scrolling a test window works. Clicking letterbox margins does not inject input.
- [ ] Move viewer focus to another control/window; remote held keys/buttons release and control returns to viewing only. A new grant requires a new request.
- [ ] Hold a remote modifier or mouse button, then use host **Revoke control — keep viewing**. The held state releases, further input stops and viewing continues.
- [ ] If the indicator advertises Ctrl+Alt+F12, test it on the host. If unavailable, use the visible revoke button. Closing the indicator also revokes control.
- [ ] Use viewer **Release control**, host **Stop sharing and disconnect**, viewer Disconnect, page departure and app close. Held remote keys/buttons must release; stopping ends the session.
- [ ] Interrupt the network during held input. After timeout/cleanup, no remote held state remains. Restoring the network does not silently regrant control.
- [ ] Change primary-display geometry/resolution/layout while controlling. Control must invalidate rather than silently remap; start a new control session afterward.
- [ ] Hold a physical host key before a matching remote press. Remote handling must not take ownership/release that already-held local key.
- [ ] Elevated applications/UAC secure desktops remain outside control; DeskSteer neither elevates nor bypasses Windows input restrictions.

Input forwarding has a 32-command queue, mouse movement is limited to roughly one event per 33 ms, and the host validates each command with a 200-per-second limit. These bounds are not measured latency guarantees. A full viewer queue releases control rather than accumulating input.

Windows cannot reliably distinguish a physical press of the same key/button after DeskSteer has already pressed it remotely. Release cleanup may affect that overlapping state; avoid simultaneous local/remote use of the same keys during this check. Layout-specific characters, IMEs and system-reserved shortcuts need separate compatibility evaluation; do not assume text/shortcut equivalence across keyboard layouts.

Report results explicitly, including any stuck modifier/button, unexpected foreground target, geometry mismatch or unavailable revoke control. Passing historical LAN checks does not accept this input milestone.
