# Connection

`ConnectionServer`, `WebSocketUpgrade`, `Handshake`, `CertificateProvider` and `EventHub` on the server, and `ServerConnection` with its `Factory` on the client, carry the protocol.

## Layers

A TCP connection is wrapped in TLS with the server's certificate, then upgraded to a WebSocket by a small hand-written HTTP upgrade (a plain HTTP request gets a short text reply and is closed). `WebSocketLineStream` presents the socket as a stream of lines, so the first line is the handshake and every later line is a JSON message, read and written by `MessageChannel`. Each stage has its own five second limit, so a connection that stalls cannot hold a slot.

## Handshake

The key is compared in fixed time. A wrong key, an overlong line or a malformed line gets `accepted: false` and a closed connection; a client that sends nothing within five seconds is closed without any reply.

## Serving a client

One loop reads requests and starts each in its own task, so a slow request never delays another, and a single semaphore serializes writes to the socket. A subscription to the `EventHub` feeds a queue that a second task drains into event lines, so events and responses interleave safely. A request that throws unexpectedly becomes a generic error response and a log line instead of dropping the connection.

## Certificate

Without a configured PFX the provider creates a self-signed certificate once, valid for ten years, naming the machine's host name and every local address, and stores it in the data folder readable only by its owner. Its fingerprint is printed at startup.

## Client side

`ServerConnection` matches responses to requests by id, so calls overlap. Events are posted to the synchronization context the connection was opened on, so view models change on the UI thread. When the socket dies, the connection reports `Lost`, retries with a growing delay up to fifteen seconds, and reports `Reconnected`, after which the app reads everything again because events were missed. The app also forces a reconnect when it returns from the background after a few seconds, since a phone often kills sockets silently. The certificate fingerprint seen on the first connection is pinned for every reconnect; a mismatch stops the retries and surfaces as a `CertificateChangedException`.
