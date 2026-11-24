# message_signed (v1)

Transport authentication for MCP over HTTP: the client (e.g. an AI gateway) signs **every** request
and asserts the acting user; the MCP server verifies the signature and applies **its own**
permissions to that user. No bearer credential travels on the wire.

## Discovery

A server announces the scheme in `GET /.well-known/mcp-connection.json` (public, unsigned):

```json
"transportAuth": { "kind": "message_signed", "audience": "example-01", "alg": "ES256" }
```

## Header

`Mcp-Message-Signature: <b64url(header)>.<b64url(payload)>.<b64url(signature)>` — compact JWS,
base64url without padding, ECDSA P-256 / SHA-256 signature in IEEE P1363 form (64 bytes, `r||s`).

JWS header: `{"alg":"ES256","typ":"mcp-msg+jwt","kid":"<kid>"}`. Any other `alg` is rejected.
`kid` = first 16 characters of base64url(SHA-256(SubjectPublicKeyInfo DER)) of the signing key.
Trusted-signers files **must** be named `<kid>.pem` with that exact stem (the loader rejects a
mismatched file name).

## Payload

| Claim | Content |
|-------|---------|
| `v` | `1` |
| `iss` | issuer name of the client (AiProxy default: `aiproxy`). Required; the server may pin an expected value (`MessageSignatureIssuer` / `ExpectedIssuer`) |
| `aud` | `transportAuth.audience` of the target server |
| `iat` / `exp` | Unix seconds; `exp - iat` ≤ 120; ±30 s clock skew |
| `jti` | unique id, single use (replays are rejected) |
| `htm` | HTTP method |
| `htp` | path + query as received by the server (`/` for the root), including ASP.NET `PathBase` when set |
| `bh` | base64url SHA-256 of the exact body bytes (of empty input when there is no body) |
| `sub` | user id in the client (omitted for service calls) |
| `username` | login name — the key the server uses for its own permissions (omitted for service calls) |
| `name`, `email` | display name and email (omitted when unknown) |
| `groups` | client-side group names |
| `roles` | `user` / `admin`, or `service` when there is no user |

### Path binding (`htp`)

The client signs `RequestUri.PathAndQuery`. The ASP.NET middleware verifies
`PathBase + Path + QueryString`. Configure `UsePathBase` (or the reverse proxy) so the path the
server sees matches what the client signed; a PathBase mismatch yields HTTP 401 (`path mismatch` in
server logs only).

## Verification (server)

In order: format, `alg`, known `kid`, signature, `v`, `aud`, `iss`, time window, `htm` / `htp`, body
hash, then `jti` (recorded only once everything else passed). Any failure → HTTP 401. The HTTP JSON
body is generic (`{"error":"unauthorized"}`); the detailed reason is logged only.

Trusted keys: one `<kid>.pem` (SPKI `PUBLIC KEY`, P-256) per client key in the server's
trusted-signers directory. Rotation: add the new key, then remove the old one.

### Replay protection (single process)

Accepted `jti` values are remembered **in memory in the verifying process**. Do not run multiple
replicas of the same MCP audience behind a load balancer with this in-process cache alone — a
captured request can be replayed on another replica. One verifying process per audience (or an
external shared replay store, not shipped in this SDK).

Modes: `off` (no verification), `optional` (unsigned requests pass; present-but-invalid → 401),
`required` (every request must be signed).

Client roles and groups are information only: authorization is the server's decision, from
`username`. An unknown user should see nothing.
