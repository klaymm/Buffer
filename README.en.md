[Русский](README.md) | [English](README.en.md)

# Buffer

![Release](https://img.shields.io/github/v/release/klaymm/Buffer) ![License](https://img.shields.io/github/license/klaymm/Buffer) ![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue) ![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512bd4)

A small Windows program that replaces the Win+V clipboard history. Its window looks like the system one, but it keeps what Windows skips: large screenshots, files and, if you want, passwords. The history survives a reboot.

## Features

- **History on Win+V** - an entry is pasted with a click or Enter, and the entry itself stays in place
- **What Windows skips** - large screenshots and files, and passwords if you choose so
- **Pinning** - pinned entries are not erased by the Clear all button and survive a reboot
- **Recycle bin** - deleted entries open with the "..." button
- **Storage on disk** - the history can be kept after a reboot, it is stored encrypted
- **Settings** - hotkey, language and autostart

## Installation

Windows 10 or 11 is required, .NET Framework 4.8 is already built into them.

1. Download `Buffer.exe` from the [Releases](https://github.com/klaymm/Buffer/releases) page.
2. Run it. No installation is needed, an icon appears in the tray.

Building from source (.NET SDK 8 or newer is required):

```
dotnet build -c Release
```

The file appears in `src\Buffer\bin\Release\Buffer.exe`.

## Usage

Win+V opens the history. A click on an entry or Enter pastes it where the cursor is, and the entry itself stays in place. The "..." button opens the recycle bin, the pin keeps an entry: pinned entries are not erased by Clear all and survive a reboot.

Everything else is in the tray icon menu:

- "Save everything, even after a reboot": remembers everything, including passwords, and keeps the history on disk in encrypted form.
- "Keep all entries": without this option only the last 50 unpinned entries are kept.
- "Run as administrator": needed if UAC (User Account Control) is on in Windows and you have to paste into windows opened as administrator. The program asks for permission once and then starts this way.
- Hotkey, language and autostart.

## Limitations

With UAC on and without administrator mode, it cannot paste into administrator windows. If the program cannot tell where the cursor is, the window appears at the mouse pointer.

## License

[MIT](LICENSE)
