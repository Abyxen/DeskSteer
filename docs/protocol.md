# Protocol status

No network protocol, listener, client or TLS connection exists in 0.2.0. No port is reserved or opened.

Milestone 3 must specify a versioned envelope, fixed byte order, message types, bounded payload lengths, timeout policy and lifecycle before implementing transport. Message framing tells a receiver where one message ends in TCP's continuous byte stream. Lengths must be checked before allocating memory.

TLS encrypts the stream and verifies peer identity only when certificate validation and the application's pairing/trust policy are correct. Never accept all certificates. Initial trust must be verified out of band or through another documented authenticated pairing procedure; a device ID alone is not proof of identity.

Exact byte layout, certificate provisioning and limits are undecided and unimplemented. Freeze them alongside tests, not as speculative code. The LAN prototype must remain off the public internet. WebRTC, ICE, NAT traversal and relays belong to Milestone 6 after stable LAN testing.
