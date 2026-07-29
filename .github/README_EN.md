<p align="center">
  <img src="https://github.com/0x1-1/RevivalDPI/blob/main/src/RevivalDPI/Resources/revivaldpi-logo.png?raw=true" alt="RevivalDPI" width="360">
</p>

<h1 align="center">RevivalDPI</h1>

<p align="center">
  A Windows desktop console for managing network routing, DPI bypass methods,
  repair actions and service cleanup from one place.
</p>

<p align="center">
  <a href="../README.md">Türkçe</a> ·
  <a href="README_RU.md">Русский</a>
</p>

---

## What is RevivalDPI?

RevivalDPI is a Windows desktop application that manages the tools used to work
around DPI (Deep Packet Inspection) blocking from a single interface. It
installs, configures, monitors and cleans up components such as WireSock,
ByeDPI, Zapret and GoodbyeDPI.

The point is that you do not have to set these tools up by hand, each with its
own command-line flags. The app ships ready-made ISP profiles, runs the setup
steps, and lets you revert what it changed.

> [!WARNING]
> RevivalDPI **runs elevated**, installs Windows services, loads a kernel-mode
> packet capture driver (WinDivert) and changes your DNS configuration. Do not
> run it without understanding what it does. Everything it changes can be undone
> from the **Servisler** (Services) screen.

## Install

Two options on the
[releases page](https://github.com/0x1-1/RevivalDPI/releases/latest):

| File | Description |
| --- | --- |
| `RevivalDPI-Setup-vX.Y.Z.exe` | Installer. Bundles the prerequisites (VC++ redist, Windows Packet Filter); the app installs them on the first setup run. **Recommended.** |
| `RevivalDPI-win-x64-vX.Y.Z.zip` | Portable. Self-contained, no separate .NET install needed. |

### Verify your download

Every release ships a `SHA256SUMS.txt` with the SHA-256 digests of the published
files:

```powershell
Get-FileHash .\RevivalDPI-win-x64-v1.6.0.zip -Algorithm SHA256
```

Compare the result with the matching line in `SHA256SUMS.txt`.

### Requirements

- Windows 10 (1809+) or Windows 11, **x64**
- Administrator privileges
- No separate .NET install for the portable build

> [!NOTE]
> Cloudflare WARP, Kaspersky and other VPN/security software hook the network
> stack and can conflict with RevivalDPI. If something misbehaves, try disabling
> them first.

## License

MIT, see [LICENSE](../LICENSE).

RevivalDPI redistributes third-party executables and libraries such as Zapret,
GoodbyeDPI, WinDivert, WireSock, ByeDPI and ProxiFyre. Their copyright and
licences belong to their respective owners.
