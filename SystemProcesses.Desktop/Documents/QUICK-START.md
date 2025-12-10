# SystemProcesses.Desktop - Quick Start Guide

## Build & Run (5 Minutes)

### Prerequisites
- Windows 10/11
- .NET 9.0 SDK installed
- Administrator privileges (recommended)

### Quick Build

```bash
cd /workspace/SystemProcesses.Desktop
dotnet restore
dotnet build
dotnet run
```

### Or Open in Visual Studio
1. Double-click `SystemProcesses.Desktop.csproj`
2. Press F5

## Using the Application

### Main Interface

```
┌─────────────────────────────────────────────────────────────────┐
│ [End Process] [End Process Tree] │ [Details] [Open Dir] │ [Isolate]│
├─────────────────────────────────────────────────────────────────┤
│ Search: [________] Refresh: [2s ▼] [Pause]                     │
├─────────────────────────────────────────────────────────────────┤
│ PID  │ Name         │ CPU    │ Mem      │ VM Size  │ Parameters │
│ ─────┼──────────────┼────────┼──────────┼──────────┼──────────  │
│ 1234 │ > chrome.exe │ 2.5%   │ 150 MB   │ 500 MB   │ --type=... │
│      │   └ child    │ 0.1%   │ 50 MB    │ 200 MB   │            │
└─────────────────────────────────────────────────────────────────┘
```

### Quick Actions

| Action | Shortcut | Description |
|--------|----------|-------------|
| **Search** | Type in search box | Filter by process name |
| **Expand/Collapse** | Click arrow | Show/hide child processes |
| **Select Process** | Click row | Enable action buttons |
| **End Process** | Toolbar button | Kill selected process |
| **Pause Updates** | Pause button | Stop auto-refresh |
| **Isolate Tree** | Isolate toggle | Show only selected tree |

### Color Coding

- **Black Text**: Regular user processes
- **Grey Text**: Windows service processes

### Tips

1. **Run as Administrator** for full process information
2. **Use Search** to quickly find processes by name
3. **Pause** before ending processes to avoid misclicks
4. **Lower refresh rate** (5s/10s) on slower systems
5. **Isolate Tree** to focus on a specific process family

## Common Use Cases

### Find and Kill a Process
1. Type process name in Search box
2. Click on the process
3. Click "End Process"
4. Confirm action

### View Process Tree
1. Find parent process
2. Click the arrow to expand
3. See all child processes

### Inspect Process Details
1. Select a process
2. Click "Show Details"
3. View comprehensive information

### Open Process Location
1. Select a process
2. Click "Open Directory"
3. Windows Explorer opens at executable location

## Troubleshooting

### "Access Denied" Errors
- Run application as Administrator
- Some system processes are protected

### Missing Icons
- Normal for some system processes
- Requires access to executable file

### Performance
- Increase refresh interval to 5s or 10s
- Use Pause when actively working
- Clear search filter when not needed

## Architecture Overview

```
User Action
    ↓
MainWindow (View)
    ↓
MainViewModel (Commands/Properties)
    ↓
ProcessService (Data Retrieval)
    ↓
Windows APIs (Process/WMI/Services)
```

## File Reference

| File | Purpose |
|------|---------|
| `MainWindow.xaml` | UI layout and binding |
| `MainViewModel.cs` | Application logic |
| `ProcessService.cs` | Process data retrieval |
| `ProcessInfo.cs` | Data model |
| `*Converter.cs` | Data formatting |

## Support

See `README.md` for full documentation and `VERIFICATION_CHECKLIST.md` for implementation details.
