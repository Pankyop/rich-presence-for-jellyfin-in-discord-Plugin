# Security Policy

## Supported Versions

Security fixes are provided for the latest published release. Older releases are
supported only on a best-effort basis; upgrading is always recommended.

| Version | Supported |
|---|---|
| 1.4.x | ✅ |
| 1.3.x and earlier | ❌ |

## Reporting a Vulnerability

Please **do not** open a public issue for a security vulnerability.

Use GitHub's private reporting flow:

1. Open the repository's **Security** tab.
2. Select **Report a vulnerability** (GitHub Private Vulnerability Reporting).
3. Include the affected version, a description, reproduction steps, and any
   relevant logs (remove secrets, tokens, media titles, and personal data).

You can expect an initial acknowledgement within 7 days. Please allow time for a
fix and coordinated disclosure before publishing details.

## Scope

In scope:

- The plugin assembly and its configuration page.
- Release artifacts, checksums, and the manifest/release pipeline.
- GitHub Actions workflows in this repository.

Out of scope:

- Jellyfin Server itself (report to the Jellyfin project).
- Discord Desktop and Discord's servers (report to Discord).
- The external metadata/artwork providers queried when the administrator
  explicitly enables `EnableExternalMetadataLookups`.
- Third-party services reachable through action buttons (IMDb, TMDb, AniList,
  MusicBrainz).

## Security-Relevant Behaviour

- The plugin talks to Discord over local IPC only.
- External metadata lookups are **disabled by default**. When enabled, media
  titles, artist names, release years, and provider IDs may be sent to IMDb,
  AniList, TVMaze, Wikipedia, Deezer, and Cover Art Archive to resolve artwork.
- `PublicServerUrl` must be HTTPS and must not contain credentials or point at a
  localhost/private address.
- Release checksums are verified against the published asset before the manifest
  is updated.

See [docs/SECURITY_AUDIT.md](docs/SECURITY_AUDIT.md) for the current threat model
and documented residual risks.
