# MacOsPublish

**MacOsPublish** is a command-line tool to build, bundle, codesign, and optionally notarize `.NET` macOS applications in a unified `.app` bundle that includes both `x64` and `arm64` binaries.

---

## 🧱 Bundle Structure

```
YourApp.app
└── Contents
    ├── MacOS
    │   ├── osx-arm64/         <- your arm64 build
    │   ├── osx-x64/           <- your x64 build
    │   ├── shared/            <- shared binaries (deduplicated)
    │   └── YourApp.sh         <- launcher script (auto-detects CPU arch)
    ├── Resources/             <- assets (icons, images, etc.)
    └── Info.plist
```

---

## ⚙️ Usage

```bash
MacOsPublish <PROJECT> [<OUTPUT_DIR>] [<SIGNING_IDENTITY>] [<INSTALLER_IDENTITY>] [--notarize <PROFILE>] [--no-restore]
```

### Arguments

| Argument                | Description                                                                 |
|------------------------|-----------------------------------------------------------------------------|
| `<PROJECT>`            | Path to `.csproj` or `.sln` file.                                           |
| `[OUTPUT_DIR]`         | Destination output folder (default: `bin/UniversalBundleApp`).              |
| `[SIGNING_IDENTITY]`   | Apple Developer identity. Empty to skip code signing.                       |
| `[INSTALLER_IDENTITY]` | Apple Installer identity (used for `.pkg` generation).                      |
| `--notarize <PROFILE>` | Submit the `.dmg` to Apple Notary Service. Requires `xcrun notarytool`.     |
| `--no-restore`         | Skip `dotnet restore`.                                                      |
| `-h`, `--help`         | Display help.                                                               |

---

## 🔐 Notarization Setup

Before using `--notarize`, run once:

```bash
xcrun notarytool store-credentials --apple-id <email> --team-id <TEAM_ID> --password <app-password> --keychain-profile "MacOsPublishProfile"
```

You can then use:

```bash
--notarize MacOsPublishProfile
```

---

## 🧠 Features

- ✅ Supports both `osx-arm64` and `osx-x64`
- ✅ Parallel builds
- ✅ Deduplicates shared files (using SHA-256)
- ✅ Creates `.app`, `.pkg`, and `.dmg`
- ✅ Optional Apple notarization and stapling
- ✅ Symbolic links to shared files
- ✅ `.DS_Store` cleanup
- ✅ Automatic architecture launcher script

---

## 📦 Requirements

- .NET SDK
- macOS with:
  - `xcrun`
  - `codesign`
  - `productbuild`
  - `hdiutil`

---

## 📄 License

MIT License — (C) 2025 Castello Branco Technologia LTDA
