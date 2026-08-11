# ZeeVault

A sleek, modern app launcher and vault for Windows. Organize your games, tools, and files in one beautiful interface.

![ZeeVault](<img width="964" height="661" alt="image" src="https://github.com/user-attachments/assets/0cc3b215-5656-4896-ac89-cc56404a7be3" />
)

## Features

- **Smart Search** — Type to search your vault or discover installed Windows apps instantly
- **Auto Icon Detection** — Automatically picks up app icons and names from your system
- **Categories** — Organize items into Games, Tools, and Files
- **Drag & Drop** — Drop any file or shortcut to add it to your vault
- **Manual Add** — Browse for any file with a clean, polished dialog
- **Quick Launch** — Click any item to launch it directly
- **Custom Titlebar** — Animated minimize, maximize, and close buttons
- **Dark Theme** — Beautiful dark UI with glassmorphism effects
- **Responsive Grid** — Cards automatically adjust to window size


## Download

1. Go to [Releases](../../releases)
2. Download the latest `ZeeVault.zip`
3. Extract and run `GameVault.exe`

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

## How It Works

- **Smart Search** scans Start Menu shortcuts, Desktop shortcuts, Registry installed programs, and Program Files to find any app on your system
- **Icon Extraction** uses Windows Shell API (`SHGetFileInfo`) and `Icon.ExtractAssociatedIcon` to pull real app icons
- **Category System** lets you tag anything as Games, Tools, or Files with color-coded badges

## License

MIT

## Author

[@charlotte-zee](https://github.com/charlotte-zee)
