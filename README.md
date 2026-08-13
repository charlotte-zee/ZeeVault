# ZeeVault

A sleek, modern app launcher and vault for Windows. Organize your games, tools, and files in one beautiful interface.

<img width="964" height="661" alt="image" src="https://github.com/user-attachments/assets/524db5f1-5236-4b37-b993-f301518c54ee" />

<img width="962" height="664" alt="image" src="https://github.com/user-attachments/assets/12e63c74-1b05-4709-908b-2e5c55ea3b4f" />



## Features

- **Smart Search** — Type to search your vault or discover installed Windows apps instantly
- **Auto Icon Detection** — Automatically picks up app icons and names from your system
- **Categories** — Organize items into Games, Tools, and Files
- **Drag & Drop** — Drop any file, shortcut, or Start Menu app to add it to your vault
- **Start Menu Drag** — Drag apps directly from the Windows Start Menu into ZeeVault
- **Manual Add** — Browse for any file with a clean, polished dialog
- **Quick Launch** — Click any item to launch it directly
- **Layout Styles** — Switch between Clean (minimal icon + title) and Cards (full details with category, extension, border) layouts via Settings
- **Auto-Update** — Automatically checks for new versions on startup, with in-app download and install
- **Settings Menu** — Access update checks, layout switching, and About info from the gear icon
- **Single Instance** — Only one ZeeVault window at a time
- **Custom Titlebar** — Animated minimize, maximize, and close buttons
- **Dark Theme** — Beautiful dark UI with glassmorphism effects
- **Responsive Grid** — Cards automatically adjust columns based on screen size


## Download

1. Go to [Releases](../../releases)
2. Download the latest `ZeeVault-Setup.exe`
3. Run the installer — installs to `C:\Program Files\ZeeVault`
4. ZeeVault will appear in your Start Menu and on your Desktop

To uninstall, go to **Control Panel > Programs and Features** or use the Start Menu shortcut.

## Build from Source

**Requirements:** .NET 10 SDK, Windows 10/11

```bash
git clone https://github.com/charlotte-zee/ZeeVault.git
cd ZeeVault
dotnet build
dotnet run
```

## Tech Stack

- C# / .NET 10
- WPF (Windows Presentation Foundation)
- WPF UI (Fluent Design)
- System.Drawing for icon extraction
- P/Invoke for shell icon APIs
- NSIS for installer packaging

## How It Works

- **Smart Search** scans Start Menu shortcuts, Desktop shortcuts, Registry installed programs, and Program Files to find any app on your system
- **Icon Extraction** uses Windows Shell API (`SHGetFileInfo`) and `Icon.ExtractAssociatedIcon` to pull real app icons
- **Start Menu Drag-and-Drop** resolves Windows Shell IDList Array and FileDrop AUMIDs to add Store and Win32 apps
- **Category System** lets you tag anything as Games, Tools, or Files with color-coded badges

## License

ZeeVault is source-available software provided under the
[ZeeVault Personal Use and Modification License](LICENSE).

You may download and use ZeeVault personally and modify your private copy
for personal improvements, fixes, customization, and experimentation.

You may also fork this repository or create a branch for the purpose of
improving ZeeVault and submit your changes to the official repository
through a Pull Request.

Independent redistribution, publication, commercial use, resale,
sublicensing, and distribution of modified versions are prohibited.

Forks and modified copies may not be distributed independently or presented
as official ZeeVault releases.

Only the official ZeeVault repository may publish or distribute official
ZeeVault releases.

Copyright © 2026 Charlotte Zee. All rights reserved.

## Contributions

Contributions and improvements are welcome.

You may fork the repository, make improvements, fixes, or features, and
submit them through a Pull Request to the official ZeeVault repository.

All contributions are subject to review and may be accepted, modified,
or rejected at the discretion of the project maintainer.

Contributing does not grant permission to independently distribute, sell,
publish, or commercially exploit ZeeVault or modified versions of it.

## Author

[@charlotte-zee](https://github.com/charlotte-zee)
