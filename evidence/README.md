# Verification evidence

`verified/` contains renders from the final passing Windows test run:

- Six actual WPF views at 1140×740 and 820×580, rendered in an isolated STA test process.
- `captured-owned-window.png` and `captured-resized-window.png`: real Windows Graphics Capture pixels from a disposable test-created window. These are not renders substituted for capture.

The view renders do not verify live desktop input or the native system picker's UI. Only test-owned content is included; no personal desktop content is stored. Earlier scratch evidence directories remain ignored by Git.
