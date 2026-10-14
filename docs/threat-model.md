# Initial threat model

This document distinguishes current local exposure from future remote threats. No network security mechanisms are claimed as implemented.

| Asset / threat | Current response | Future requirement |
|---|---|---|
| Desktop pixels | Local capture via Windows picker; bounded frames; stop/disposal; no transmission | Approved-session enforcement and immediate revocation before remote transmission |
| Keystrokes | Never acquired | Explicit control approval and input authorization |
| Peer impersonation | No peers | Authenticated TLS and documented device trust/pairing |
| Malformed network lengths | No transport | Validate length before allocation; bounded queues and fuzz tests |
| Resource exhaustion | Settings/diagnostics/frame queues bounded; capture dimensions checked | Frame/message/rate limits and slow-consumer policy |
| Sensitive diagnostic leakage | Allowlisted events, no payload/exception formatting | Review all new event fields before adding |
| Corrupt local preferences | Validation error, defaults, explicit save | Never use this file for security authorization |
| Local same-user modification | Outside this prototype's protection | Protect identity keys using established Windows facilities |
| Relay abuse / internet scanning | No internet service | Authenticated relay allocation, expiry, rate limits and monitoring |

Never bypass UAC, hide control sessions, capture covertly or install persistence implicitly. Define security boundaries and abuse cases before adding remote input or unattended access.
