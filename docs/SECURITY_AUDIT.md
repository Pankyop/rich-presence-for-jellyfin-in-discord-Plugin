# Security & Privacy Review

**Review date:** 2026-10-26

**Scope:** plugin source (`Jellyfin.Plugin.DiscordRichPresence`), configuration
page, build/release scripts, GitHub Actions workflows, manifest, and published
release artifacts.

**Version reviewed:** 1.3.0.0 and the 1.4.0.0 hardening changes.

This document describes the current design and known residual risks. It is not a
third-party certification and does not claim that the plugin is free of defects.

---

## 1. Architecture and Data Flow

```
Jellyfin ISessionManager
        │
        ▼
SessionMonitor ──► ActivityBuilder ──► DiscordIpcClient
        │                                │
        │ optional, opt-in               │ local IPC only
        ▼                                ▼
ArtworkResolver ──► external metadata   Discord Desktop
                    providers
```

### Local IPC

- Windows: named pipes `\\.\pipe\discord-ipc-0` … `discord-ipc-9`.
- Linux/macOS: Unix domain sockets in `$XDG_RUNTIME_DIR`, the Flatpak app
  directory, or (only when `XDG_RUNTIME_DIR` is unset) temporary directories.

The plugin sends the current media activity to the locally running Discord
client. Discord then publishes it to the user's profile according to Discord's
own privacy settings.

### External metadata lookups (opt-in)

`EnableExternalMetadataLookups` defaults to **false**. When disabled, the plugin
does not make any outbound HTTP request to metadata providers. It uses only:

- the administrator-configured `PublicServerUrl` (HTTPS), or
- bundled/static fallback image URLs that Discord itself may fetch.

When explicitly enabled, `ArtworkResolver` may query:

| Provider | Endpoint | Data sent |
|---|---|---|
| IMDb suggestion API | `v3.sg.media-imdb.com` | title, release year, IMDb ID |
| AniList GraphQL | `graphql.anilist.co` | anime title |
| TVMaze | `api.tvmaze.com` | series title |
| Wikipedia REST | `en.wikipedia.org` | media title |
| Deezer | `api.deezer.com` | artist and track name |
| Cover Art Archive | `coverartarchive.org` | MusicBrainz release/release-group ID |

Requests are made only after a Discord IPC connection is established, so a closed
Discord client does not generate metadata traffic. Responses are size-limited
(512 KB), parsed with a bounded JSON depth, and cached for 24 hours in a
512-entry cache.

The server's public IP address is necessarily visible to those providers when a
lookup is made. Media titles and provider IDs are also visible to them. This is
why the feature is opt-in.

### Images and Discord's proxy

Discord fetches activity images through its own CDN/proxy. A URL that points at
`localhost` or a private address is therefore useless to Discord and may be
rejected by its proxy. `PublicServerUrl` must be a public HTTPS URL; provider
image URLs are restricted to known artwork CDNs over HTTPS.

---

## 2. Security Controls

### Input handling

- Media titles, artist names, and episode text are sanitized and truncated to
  Discord's 128-character limits before transmission.
- Provider IDs used in action-button URLs are validated (`tt` + digits for IMDb,
  digits for TMDb/AniList, UUID for MusicBrainz).
- JSON is parsed with `JsonDocument`; there is no polymorphic or type-name-based
  deserialization.
- IPC payloads are limited to 64 KB and are never deserialized into objects; only
  the opcode is inspected.

### URL validation

`ArtworkResolver.TryGetPublicBaseUrl` and `IsPublicUrl`:

- require HTTPS;
- reject embedded credentials (`user:pass@`);
- reject query strings and fragments in the configured base URL;
- reject `localhost`, `.localhost`, single-label hostnames, and internal suffixes
  (`.local`, `.internal`, `.home.arpa`, `.lan`, `.test`, `.invalid`, `.example`,
  `.onion`);
- reject loopback, private, link-local, CGNAT, multicast, reserved, documentation,
  IPv4-mapped, ULA, and IPv6 link-local addresses;
- resolve DNS names and reject the URL if any resolved address is private.

Provider-returned image URLs must be HTTPS, credential-free, resolve to a public
address, and match a known artwork CDN suffix.

### IPC hardening

- Discord Application IDs must be 17–20 digit numeric snowflakes.
- Every IPC read and write has a 5-second timeout; a stalled peer closes the
  connection and triggers the retry backoff.
- PING frames are answered with PONG; CLOSE frames are logged with sanitized,
  length-limited payloads.
- On Windows, the named-pipe server PID is checked and the pipe is rejected if the
  owning process name does not contain `discord` (when the PID can be obtained).
- On Linux/macOS, `/tmp` and other shared directories are not used when
  `XDG_RUNTIME_DIR` is set.
- Log messages strip control characters from external payloads to prevent log
  injection.

### Configuration and lifecycle

- `PublicServerUrl`, `PauseGracePeriodMinutes`, and `PollIntervalSeconds` are
  validated/clamped by both the UI and the backend.
- Pause state is guarded by a dedicated lock; session events are dispatched
  through an exception-safe wrapper instead of unguarded `async void`.
- The hosted service unsubscribes events, cancels polling, and clears the Discord
  activity with a bounded timeout during shutdown.
- `TargetUserId` filters sessions by username or user ID. Leaving it empty is
  supported for single-user servers but allows any session to drive the presence
  on a shared server; the UI warns about this.

### Release and supply chain

- Release ZIPs contain exactly one file: the plugin DLL.
- MD5 (required by Jellyfin) and SHA256 are computed from the ZIP.
- `publish_release.py` downloads the uploaded asset again, compares size, MD5,
  SHA256, and GitHub's asset digest, and only then updates `manifest.json`.
- `verify_release.py` independently verifies every manifest entry (and the
  checksums in `RELEASE_NOTES.md`) against the published assets.
- Release workflow jobs have least-privilege permissions; build steps run without
  a write token and without persisted checkout credentials.
- Third-party GitHub Actions are pinned to full commit SHAs.
- Dependabot tracks NuGet and GitHub Actions updates.
- The .NET SDK is pinned via `global.json`, NuGet dependencies are locked, and
  vulnerable transitive packages (`System.Text.Json`, `System.Text.Encodings.Web`)
  are overridden with fixed versions.

---

## 3. Threat Model and Residual Risks

| Threat | Impact | Mitigation | Residual risk |
|---|---|---|---|
| Remote code execution via plugin endpoints | Critical | No HTTP listener, no remote deserialization, no dynamic code loading | Low |
| Malicious local process impersonates Discord IPC | Medium | User-private socket directory; Windows pipe owner check; timeouts | A same-user process can still impersonate Discord on platforms where peer credentials are unavailable |
| Metadata provider returns a malicious image URL | Medium | HTTPS-only, public-host and artwork-CDN allowlist | A compromised approved CDN could serve unwanted content to Discord |
| DNS rebinding / public hostname resolving internally | Medium | DNS resolution checks all addresses for `PublicServerUrl` | Provider hostnames are resolved by the HTTP stack; DNS changes after validation are not re-checked |
| External metadata disclosure | Medium (privacy) | Disabled by default; explicit UI opt-in; documented providers and data | Users who enable it accept the disclosure |
| Disk/memory growth from artwork cache or HTTP responses | Low | 512-entry cache, 512 KB response cap, 24 h TTL | Provider-controlled JSON can still consume up to the cap |
| Log injection via IPC payload | Low | Control characters stripped, payloads length-limited | Log content can still contain arbitrary printable text |
| Release asset tampering or checksum drift | High | Post-upload download verification; independent manifest verifier; manifest updated only from verified asset | A compromised GitHub account or maintainer machine remains authoritative |
| Command injection in release workflow | High | Tag passed via `env`, validated against `^vX.Y.Z.W$`, never interpolated into shell source | A malicious commit in the checked-out repository can still execute its own build scripts |
| Vulnerable dependency | Medium | Locked restore, vulnerability gate, direct patched versions | New advisories may appear between runs |
| Reproducibility drift | Low | Deterministic build settings, pinned SDK, locked packages | Bit-for-bit reproducibility across machines is not guaranteed |

The previous audit incorrectly stated that the plugin made no external network
requests and that IPC hijacking was fully mitigated by OS access controls. Those
claims have been removed; this document describes the actual behaviour and the
remaining limitations.

---

## 4. What Was Fixed in 1.4.0.0

- Corrected manifest and release-note checksums for 1.3.0.0, 1.2.0.0, 1.0.1.0,
  and 1.0.0.0.
- Made external metadata lookups opt-in (default off) and documented the data
  flow.
- Required HTTPS and public hosts for `PublicServerUrl`; closed IPv6, CGNAT,
  `0.0.0.0`, userinfo, and DNS-name bypasses in `IsPublicUrl`.
- Added URL validation for provider-returned images.
- Added IPC read/write timeouts, PING/PONG handling, application-ID validation,
  pipe-owner verification on Windows, and user-private socket directory
  preference on Unix.
- Bounded the artwork cache and HTTP response size.
- Fixed the pause-grace `0` UI bug and configuration default handling.
- Hardened the release workflow (tag validation, tag checkout, least privilege,
  pinned actions, post-upload verification, manifest update only after
  verification).
- Pinned the SDK, locked NuGet packages, and removed known vulnerable transitive
  versions.

---

## 5. Operational Recommendations

- Set `TargetUserId` on multi-user servers.
- Keep `EnableExternalMetadataLookups` disabled unless remote artwork is required.
- Use an HTTPS reverse proxy for `PublicServerUrl`; never embed credentials in it.
- Keep Jellyfin, Discord, and the plugin updated.
- Verify release checksums with `python scripts/verify_release.py` before manual
  installation.
- Enable GitHub branch protection and a protected `release` environment for the
  release workflow.
