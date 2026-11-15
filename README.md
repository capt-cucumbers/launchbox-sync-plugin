# LaunchBox External Game Sync Plugin

[![CI](https://github.com/capt-cucumbers/launchbox-sync-plugin/actions/workflows/ci.yml/badge.svg)](https://github.com/capt-cucumbers/launchbox-sync-plugin/actions/workflows/ci.yml)
[![Release](https://github.com/capt-cucumbers/launchbox-sync-plugin/actions/workflows/release.yml/badge.svg)](https://github.com/capt-cucumbers/launchbox-sync-plugin/actions/workflows/release.yml)
[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D6.svg)](https://www.microsoft.com/windows)

C# WPF plugin for LaunchBox that syncs game records and media from external JSON manifests directly inside the LaunchBox desktop app.

## What It Does

- Adds **Tools > Sync External Games** (with per-source quick actions and **Sync ALL Games**) to run manifest syncs from inside LaunchBox.
- Opens a management dialog showing each source's manifest status and game count, with a per-source dropdown for **Games Only**, **Media Only**, or **Games & Media**.
- Adds a right-click **Sync External Game** submenu on games that have a `Game ID` custom field (`Sync Game Data`, `Sync Game Media`, or `Sync Game Data & Media`).
- Shows live progress output and errors in a progress dialog themed to match LaunchBox.

---

## Requirements

**Windows only.** Targets LaunchBox's WPF desktop plugin API.

### Runtime
- Windows 10/11 (64-bit)
- LaunchBox 13.x or higher

### Building from Source
- [.NET SDK 9.0+](https://dotnet.microsoft.com/download/dotnet/9.0)

## Installation & Build

### Option 1: Pre-compiled Release

1. Download the latest `launchbox-sync-plugin.zip` release asset.
2. Extract `LB External Game Sync Plugin` into your LaunchBox `Plugins\` folder:
   ```text
   C:\LaunchBox\Plugins\LB External Game Sync Plugin\
   ```
3. Run `setup.bat` inside that folder to copy `config.dist.json` to `config.json`.

### Option 2: Compiling from Source

1. Build the plugin and optionally run the test suite:
   ```powershell
   dotnet build -c Release
   dotnet test tests\LaunchBoxSyncPlugin.sln -c Release
   ```
2. Copy the compiled `releases\LB External Game Sync Plugin\` folder into `C:\LaunchBox\Plugins\`.
3. Run `setup.bat` inside `C:\LaunchBox\Plugins\LB External Game Sync Plugin\` to create `config.json` from `config.dist.json`.

### Plugin Files

| File | Live File | Purpose |
| :--- | :--- | :--- |
| `config.dist.json` | `config.json` | Manifest keys, display names, JSON paths, and optional `source_name` overrides. |
| `manifest.dist.json` | — | Reference example of the expected v2 manifest format (not copied by `setup.bat`). |

## Configuration

Edit `config.json` inside the plugin folder to point to your v2 manifest files:

```json
{
  "windows_local": {
    "manifest_name": "Windows - Local",
    "manifest_path": "C:\\Path\\To\\GameBox\\data\\sources\\local\\manifest.json"
  },
  "windows_steam": {
    "manifest_name": "Windows - Steam",
    "manifest_path": "C:\\Path\\To\\GameBox\\data\\sources\\steam\\manifest.json"
  }
}
```

You can also add `"source_name"` on an entry to override the LaunchBox `Source` display name mapped from `game_source`.

---

## Usage

1. Open LaunchBox.
2. Open **Tools > Sync External Games** to sync a single source directly, run **Sync ALL Games**, or open the **Manage External Game Sync** window.
3. In the management window, choose **Games Only**, **Media Only**, or **Games & Media** for a source and click **Sync** (use **Refresh Config** after editing `config.json`).
4. To sync individual titles, right-click selected games with a `Game ID` custom field and pick **Sync External Game**.

## Related Projects

- **[`game-box`](https://github.com/capt-cucumbers/game-box)** — Desktop/web game library manager that generates v2 manifest files across stores.
- **[`launchbox-game-updater`](https://github.com/capt-cucumbers/launchbox-game-updater)** — Standalone CLI/TUI tool for pushing manifests and artwork into LaunchBox XMLs while LaunchBox is closed.
- **[`exo-unpacker`](https://github.com/capt-cucumbers/exo-unpacker)** — Extracts eXo retro collections and writes compatible v2 manifests.
