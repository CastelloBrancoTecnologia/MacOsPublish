# MacOsPublish

**MacOsPublish** is a command-line tool to build, bundle, codesign, and optionally notarize `.NET` macOS applications in a unified `.app` bundle that includes both `x64` and `arm64` binaries.

---

## 🕸️ Projet Web site

https://github.com/CastelloBrancoTecnologia/MacOsPublish

---

## 📦 nuget package 

https://www.nuget.org/packages/MacOsPublish

---

## 🛠️ Installation

To install MacOsPublish as a global .NET tool:

```bash
dotnet tool install --global MacOsPublish
```

Or update:

```bash
dotnet tool update --global MacOsPublish
```

You can also install it locally in your project:

OBS: If you cant run macospublish after installation please check if you path isnt configured 

You can configure path if your shell is zsh with command like 

```bash
    echo 'export PATH=&quot;$PATH:$HOME/.dotnet/tools&quot;' &gt;&gt; ~/.zshrc
```


```bash
dotnet tool install MacOsPublish
```

---

To check if the tool is available:

```bash
macospublish --help
```

## 🧱 Bundle Structure Generated

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
macospublish <PROJECT>  [...options]
```

### Arguments

| Argument                          | Description                                                               |
|-----------------------------------|---------------------------------------------------------------------------|
| `<PROJECT>`                       | Path to `.csproj` or `.sln` file.                                         |
| `--output <OUTPUT_DIR>`           | Destination output folder (default: `bin/UniversalBundleApp`).            |
| `--identity <IDENTITY>`           | Apple Developer identity. Empty to skip code signing.                     |
| `--installer-identity <IDENTITY>` | Apple Installer identity (used for `.pkg` generation).                    |
| `--notarize <PROFILE>`            | Submit the `.dmg` to Apple Notary Service. Requires `xcrun notarytool`.   |
| `--plist-dir <path>`              | the directory of plist files (info/entitlements) if not in current dir.`. |
| `--AssemblyVersion <version>`     | the version to put in AssemblyVersion.                                    |
| `--dry-run`                       | Dont Generate Any Files.                                                  |
| `-h`, `--help`                    | Display help.                                                             |

---

## 🔐 Notarization Setup

To notarize your app, first store your credentials:

```bash
xcrun notarytool store-credentials --apple-id <email> --team-id <TEAM_ID> --password <app-password> --keychain-profile "MacOsPublishProfile"
```

You can then use:

```bash
macospublish YourApp.csproj --notarize MacOsPublishProfile
```

---

## 🔗 Example

```bash
macospublish MyApp.csproj publish/MyApp "Developer ID Application: Your Name (TEAMID)" "Developer ID Installer: Your Name (TEAMID)" --notarize MacOsPublishProfile
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
