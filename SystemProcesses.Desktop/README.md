# SystemProcesses.Desktop - .NET 9 WPF Application

A comprehensive Windows WPF application that displays running system processes in a hierarchical tree structure with real-time monitoring capabilities.

## Features

### Display Columns
1. **PID** - Process ID (integer)
2. **Name** - Process name with icon (service processes displayed in grey)
3. **CPU** - CPU usage percentage (e.g., "0.12%")
4. **Mem** - Working set memory with smart formatting (KB/MB/GB)
5. **VM Size** - Virtual memory size with smart formatting (KB/MB/GB)
6. **Parameters** - Command line arguments

### Core Features
- **Hierarchical Process Tree** - Parent-child process relationships with expand/collapse
- **Configurable Refresh Rate** - Options: 1s, 2s, 5s, 10s with Pause/Resume
- **Search Filter** - Case-insensitive filtering on process names
- **Tree Isolation** - Show only selected process and its descendants
- **Alphabetical Sorting** - All processes sorted by name
- **Service Detection** - Service processes automatically colored grey

### Action Toolbar
- **End Process** - Terminate the selected process
- **End Process Tree** - Terminate selected process and all children
- **Show Details** - Display comprehensive process information
- **Open Directory** - Open Windows Explorer at process location
- **Isolate Tree** - Toggle filter for selected process tree

## Architecture

### MVVM Pattern Implementation

```
SystemProcesses.Desktop/
├── App.xaml / App.xaml.cs              # Application entry point
├── MainWindow.xaml / MainWindow.xaml.cs # Main UI window
├── Models/
│   └── ProcessInfo.cs                   # Process data model
├── ViewModels/
│   ├── MainViewModel.cs                 # Main view logic
│   ├── ProcessItemViewModel.cs          # Tree item wrapper
│   └── RelayCommand.cs                  # ICommand implementation
├── Services/
│   ├── IProcessService.cs               # Process service interface
│   └── ProcessService.cs                # Process data retrieval
├── Converters/
│   ├── BytesToAutoFormatConverter.cs    # Memory formatting
│   ├── CpuPercentageConverter.cs        # CPU formatting
│   └── BoolToBrushConverter.cs          # Service color converter
└── Resources/
    └── Styles.xaml                      # UI styles and resources
```

## Requirements

- **Operating System**: Windows 10/11
- **.NET SDK**: .NET 9.0 or later
- **NuGet Packages**:
  - CommunityToolkit.Mvvm (8.3.2)
  - System.Management (9.0.0)
  - System.ServiceProcess.ServiceController (9.0.0)

## Building the Application

### Option 1: Using .NET CLI

```bash
cd /workspace/SystemProcesses.Desktop
dotnet restore
dotnet build --configuration Release
```

### Option 2: Using Visual Studio

1. Open `SystemProcesses.Desktop.csproj` in Visual Studio 2022 or later
2. Build > Build Solution (Ctrl+Shift+B)

## Running the Application

### From Command Line

```bash
cd /workspace/SystemProcesses.Desktop
dotnet run
```

### From Build Output

```bash
cd /workspace/SystemProcesses.Desktop/bin/Release/net9.0-windows
./SystemProcesses.Desktop.exe
```

### From Visual Studio

Press F5 or click Start Debugging

## Key Implementation Details

### Process Tree Building
The application uses WMI (Windows Management Instrumentation) to:
- Retrieve parent-child process relationships
- Extract command line parameters
- Build hierarchical tree structure

### Service Detection
Uses `ServiceController.GetServices()` to identify which processes are Windows services and color them grey.

### Icon Extraction
Extracts process icons using `System.Drawing.Icon.ExtractAssociatedIcon()` with proper error handling for access-denied scenarios.

### CPU Calculation
Simplified CPU percentage calculation:
```csharp
cpuPercentage = (totalProcessorTime / processUptime / processorCount) * 100
```

### Memory Formatting
Smart formatting logic:
- >= 1 GB: Display in GB with 2 decimal places
- >= 1 MB: Display in MB with 2 decimal places
- < 1 MB: Display in KB with 2 decimal places

### Async/Await Pattern
All process data retrieval is performed asynchronously to prevent UI blocking during refresh operations.

## Error Handling

The application gracefully handles:
- **Access Denied**: System processes that require elevated permissions
- **Process Exit**: Processes that terminate during data collection
- **WMI Failures**: Fallback to basic process information if WMI is unavailable
- **Invalid Operations**: Protection against null reference and invalid process IDs

## Security Considerations

- **Elevated Privileges**: Some operations (ending processes, accessing system processes) may require administrator rights
- **Safe Defaults**: All destructive operations require user confirmation
- **Exception Handling**: All external calls (Process, WMI, Services) are wrapped in try-catch blocks

## Performance Optimizations

1. **Async Loading**: Process tree built asynchronously
2. **Configurable Refresh**: User can adjust refresh rate based on needs
3. **Pause Capability**: Ability to pause updates for stable viewing
4. **Lazy Icon Loading**: Icons loaded during tree building, not on every refresh

## Testing Notes

Due to the Windows-specific nature of this application:
- Requires Windows OS to run
- Uses platform-specific APIs (Process, WMI, ServiceController)
- Best tested on Windows 10/11 with various privilege levels

## Known Limitations

1. **Platform**: Windows-only (uses Win32 APIs via System.Management)
2. **Access**: Some system processes require administrator privileges to view full details
3. **CPU Accuracy**: Simplified CPU calculation (not rolling average over time)
4. **Icon Loading**: May be slow for processes with large executables

## Future Enhancements

Potential improvements for future versions:
- More accurate CPU usage tracking with performance counters
- Export process tree to file (JSON/XML/CSV)
- Process priority and affinity modification
- Memory working set visualization
- Network activity per process
- Disk I/O monitoring

## License

This is a demonstration application for educational purposes.

## Support

For issues or questions, refer to the inline code documentation and .NET 9 WPF documentation.
