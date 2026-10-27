# Xplore

A tabbed file explorer for Windows, written in C# with WPF. It looks and behaves like the classic Windows Explorer, with tabs, a folder tree, an integrated PowerShell console and a folder synchronization wizard.

## Features

- **Tabs** with a folder tree, breadcrumb address bar, back/forward/up navigation and bookmarks
- **Details view** with sorting, type-ahead selection, drag and drop, cut/copy/paste and the usual shell context menu actions, including "Open with" and "Send to"
- **Search** in the current folder with glob patterns
- **Recycle Bin** browsing, restoring and emptying
- **Undo/redo** (Ctrl+Z / Ctrl+Y) over a tree-shaped action history that keeps every branch
- **Console**: a PowerShell session in the current folder, with the selected paths available as `$selection`
- **Folder sync wizard**: scan, diff, plan and run a one-way synchronization, with dry run, hash verification, quarantine and resumable runs; diffs and plans can be exported and imported
- **Settings window** for language, path separators, history depth, start location and more
- **Localization** into 27 languages, selectable in the settings

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build, or the .NET 10 Desktop Runtime to run a published build

## Build, run and test

```
dotnet build
dotnet run --project src/Xplore.App
dotnet test
```

## Settings

Settings are stored as YAML in `%APPDATA%\Xplore\settings.yaml`; omitted keys take their defaults. The gear button opens the settings window, which also has a button to open the file in your editor.

| Key | Default | Meaning |
| --- | --- | --- |
| `Language` | system language | Culture name of the UI language, such as `de` or `pt` (applies after a restart) |
| `ForwardSlashes` | `false` | Show and copy paths with `/` instead of `\` |
| `NewItemPrompt` | `false` | Ask for the name when creating a new folder or file |
| `FoldersFirst` | `true` | List folders before files; `false` sorts them together |
| `DateFormat`, `LongTimeFormat`, `ShortTimeFormat` | none | .NET patterns such as `yyyy-MM-dd`, `HH:mm:ss`, `HH:mm`; none uses the operating system setting |
| `HistoryDepth` | `100` | Maximum length of the undo history path |
| `PrefixTimeoutMs` | `1000` | Longest pause between typed characters of one type-ahead search |
| `StartLocation` | `Default` | What opens at startup: `Default` (drive list), `Restore` (previous tabs) or `Custom` (a folder) |

Bookmarks, remembered "Open with" applications and the last session are stored next to it in `bookmarks.yaml`, `openWith.yaml` and `session.yaml`.

## Translations

All user-visible text lives in `languages/en.yaml`, a flat map from key to text. A translation is a file `languages/<culture>.yaml` (for example `fr.yaml` or `pt-BR.yaml`) with the same keys; the most specific name wins and a missing key shows the English text. The files are copied next to the executable, so no code changes are needed to add a language. Placeholders such as `{0}` must be kept.

The translations were machine-generated and have not been reviewed by native speakers; corrections are welcome.

## Project layout

| Path | Contents |
| --- | --- |
| `src/Xplore.Core` | File model, navigation, file operations, history, shell interop, settings, localization |
| `src/Xplore.Sync` | Directory diff, plan and execution engine |
| `src/Xplore.App` | WPF user interface and view models |
| `tests/Xplore.Tests` | xUnit tests |
| `languages` | UI text, one YAML file per language |

## License

[MIT](LICENSE)
