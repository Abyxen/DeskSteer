# Security and privacy

## Implemented

- No networking, remote input, startup registration, unattended access or privilege elevation.
- Local screen capture uses explicit Windows picker selection in the application; the default capture border/cursor behavior is retained. Stop clears the preview and disposes resources. Pixels are not transmitted or exported.
- Capture processing and preview dimensions are bounded; native surfaces above 16 megapixels are rejected. The user confirmed manual picker/display acceptance; native acquisition tests use only disposable test-owned windows.
- Settings input is bounded to 4096 bytes before deserialization; invalid JSON is surfaced as a recoverable UI error.
- Preference writes use a temporary file and replacement; cancellation cleans the temporary file.
- Diagnostic records use a fixed enum. Export excludes user-controlled strings, machine identity, paths, credentials and image/input content.
- The diagnostic queue retains at most 200 records. Export requires selecting a destination.
- Git excludes build outputs, keys and certificates.

## Not implemented

TLS identity verification, pairing, host approval enforcement, input authorization, replay protection, connection throttling, session expiry and protocol bounds. These must exist before their respective remote features can be accepted. There is no security guarantee or independent review.

Local preferences are not encrypted and contain only a startup-page boolean. They are not a store for trusted device keys or passwords. Normal application launch stores them in `%LOCALAPPDATA%/ProjectRemote`; the workspace helper overrides this to workspace test data.

Dependencies verified via published NuGet metadata: Microsoft.Extensions.DependencyInjection 10.0.0 and Microsoft.Extensions.Logging 10.0.0 (Microsoft .NET, MIT); xunit 2.9.3 (Apache-2.0); xunit.runner.visualstudio 3.1.5 (Apache-2.0); Microsoft.NET.Test.Sdk 17.14.1 (MIT). These are pinned compatible versions, not a claim that each is the newest available. Review updates and advisories before release.

Sources: https://dotnet.microsoft.com/en-us/download/dotnet/10.0 and the package pages at https://www.nuget.org/packages/ . Restore/build confirmed compatibility for this milestone.
