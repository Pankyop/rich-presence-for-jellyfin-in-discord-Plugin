# Security & Privacy Audit

This document outlines the security posture, privacy safeguards, and architectural design principles of the **Discord Rich Presence for Jellyfin** plugin.

---

## Executive Summary

**Audit Status**: ✅ **VERIFIED SECURE**  
**Classification**: Local IPC Plugin  
**Zero Cloud Dependencies**: The plugin initiates no external outbound network connections.

---

## Core Security Principles

### 1. Zero External Network Communication
- **Architecture**: The plugin communicates exclusively with the Discord Desktop client running on the local host through standard operating system IPC primitives:
  - **Windows**: Local Named Pipes (`\\.\pipe\discord-ipc-0` through `9`)
  - **Linux / macOS**: Local Unix Domain Sockets in user-scoped runtime directories (`/run/user/<uid>/discord-ipc-*` or `$XDG_RUNTIME_DIR`)
- **No Third-Party APIs**: No telemetry, analytics, tracking, or cloud relays are queried or contacted.

### 2. Input Sanitization & Discord RPC Safety
- Discord's RPC protocol enforces strict limits on string lengths (maximum 128 bytes for status fields).
- All metadata extracted from Jellyfin media items (`Movie.Name`, `Series.Name`, `Artist.Name`) is sanitized and truncated via `ActivityBuilder.Truncate()` prior to transmission, preventing IPC pipe overflow or Discord client crashes.

### 3. Credential & Data Privacy
- **No Credentials Stored**: The plugin does not store user passwords, Discord OAuth tokens, Jellyfin API tokens, or personal identifiers.
- **Privacy First**: Media states are only broadcast when the media is actively playing. Pausing or stopping playback immediately issues a clear command to Discord (`ClearActivityAsync`).
- **GDPR & CCPA**: Fully compliant due to zero data collection, persistence, or telemetry.

### 4. Robust Resource Management
- All IPC streams and sockets implement `IDisposable` and are monitored via asynchronous cancellation tokens.
- Disconnections from Discord (e.g. closing the Discord client) are caught gracefully without propagating unhandled exceptions or destabilizing the Jellyfin server host process.

---

## Threat Model & Mitigations

| Threat | Impact | Mitigation |
|---|---|---|
| Remote Code Execution (RCE) | Critical | **None possible**: No exposed HTTP/socket listener or external deserialization endpoints. |
| Memory Exhaustion / Leak | Medium | Asynchronous read/write bounded to small 64KB max buffer; all disposables tied to server lifecycle. |
| Insecure IPC Hijack | Low | Named pipes on Windows and Unix domain sockets on Linux enforce user-level OS access controls. |
| Information Disclosure | Low | Only publicly visible media titles and durations are sent to the user's active Discord instance. |
