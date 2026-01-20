# Dependencies Documentation

This document lists all NuGet packages used in the SystemProcesses project, their versions, purposes, and usage patterns.

---

## Table of Contents

1. [Core Framework Dependencies](#core-framework-dependencies)
2. [MVVM & UI Dependencies](#mvvm--ui-dependencies)
3. [Logging Dependencies](#logging-dependencies)
4. [Performance & Utilities](#performance--utilities)
5. [Windows-Specific Dependencies](#windows-specific-dependencies)
6. [Dependency Update Policy](#dependency-update-policy)

---

## Core Framework Dependencies

### .NET 9.0 (net9.0-windows)

**Version**: 9.0  
**Type**: Framework  
**Purpose**: Base framework for the application

**Why .NET 9**:
- Latest performance improvements (Span<T>, stackalloc optimizations)
- Enhanced `LibraryImport` source generators for P/Invoke
- Improved GC performance for low-allocation scenarios
- Modern C# 12 language features

**Migration Note**: Upgrading to .NET 10+ should be straightforward as we use stable APIs.

---

### Windows Presentation Foundation (WPF)

**Version**: Included with .NET 9  
**Type**: Framework Component  
**Purpose**: UI framework

**Configuration**:
```xml
<UseWPF>true</UseWPF>
```

**Key Features Used**:
- Data binding and MVVM pattern
- TreeView with virtualization
- XAML-based UI definition
- Hardware-accelerated rendering

---

## MVVM & UI Dependencies

### CommunityToolkit.Mvvm

**Version**: 8.4.0  
**NuGet**: https://www.nuget.org/packages/CommunityToolkit.Mvvm/  
**License**: MIT  
**Purpose**: MVVM pattern implementation via source generators

**Why This Package**:
- Zero-allocation source-generated `INotifyPropertyChanged` implementation
- Eliminates boilerplate code (70% reduction in ViewModel code)
- Excellent performance (no reflection at runtime)
- Official Microsoft Community Toolkit

**Usage in Project**:
```csharp
// ViewModels inherit from ObservableObject
public partial class MainViewModel : ObservableObject
{
    // Source-generated property with change notification
    [ObservableProperty]
    private string searchText = string.Empty;
    
    // Source-generated command
    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Implementation
    }
}
```

**Key Attributes Used**:
- `[ObservableProperty]` - Generates property with `INotifyPropertyChanged`
- `[RelayCommand]` - Generates `ICommand` implementation
- `[NotifyPropertyChangedFor]` - Cascades property change notifications
- `[NotifyCanExecuteChangedFor]` - Updates command CanExecute state

**Files Using This**:
- `ViewModels/MainViewModel.cs`
- `ViewModels/ProcessItemViewModel.cs`
- `ViewModels/StatsViewModel.cs`

---

### CommunityToolkit.HighPerformance

**Version**: 8.4.0  
**NuGet**: https://www.nuget.org/packages/CommunityToolkit.HighPerformance/  
**License**: MIT  
**Purpose**: High-performance helpers and extensions

**Why This Package**:
- Provides `Span<T>` and `Memory<T>` helpers
- `ArrayPoolBufferWriter<T>` for efficient buffer management
- Guard utilities for parameter validation
- Optimized collection helpers

**Usage in Project**:
```csharp
using CommunityToolkit.HighPerformance;

// Example: Safe span operations
ReadOnlySpan<byte> data = /* ... */;
var slice = data.Slice(0, 16);
```

**Note**: Currently lightly used; kept for future optimizations.

---

### H.NotifyIcon.Wpf

**Version**: 2.3.2  
**NuGet**: https://www.nuget.org/packages/H.NotifyIcon.Wpf/  
**License**: MIT  
**Purpose**: System tray icon integration for WPF

**Why This Package**:
- Pure WPF implementation (no Windows Forms dependency)
- Supports XAML-based context menus
- Data binding compatible
- Better than mixing WPF + WinForms `NotifyIcon`

**Usage in Project**:
```xaml
<Window xmlns:tb="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <tb:TaskbarIcon x:Name="TrayIcon"
                    IconSource="{Binding CpuTrayIconImageSource}"
                    ToolTipText="System Processes"
                    LeftClickCommand="{Binding ShowWindowCommand}">
        <tb:TaskbarIcon.ContextMenu>
            <ContextMenu>
                <MenuItem Header="Exit" Command="{Binding ExitCommand}" />
            </ContextMenu>
        </tb:TaskbarIcon.ContextMenu>
    </tb:TaskbarIcon>
</Window>
```

**Features Used**:
- Dynamic icon updates (CPU usage indicator)
- Context menu with command binding
- Window show/hide integration

**Files Using This**:
- `MainWindow.xaml`
- `ViewModels/MainViewModel.cs`

---

## Logging Dependencies

### Serilog

**Version**: 4.3.0  
**NuGet**: https://www.nuget.org/packages/Serilog/  
**License**: Apache-2.0  
**Purpose**: Structured logging framework

**Why Serilog**:
- Structured logging (log properties, not just strings)
- Rich ecosystem of sinks and enrichers
- Async file writing (minimal performance impact)
- Better than `Microsoft.Extensions.Logging` for desktop apps

**Configuration**:
See `App.xaml.cs` for actual implementation. Key settings:
- `MinimumLevel.Warning()` - Performance: Only log warnings/errors to save I/O
- `Enrich.WithThreadId()` and `Enrich.WithProcessId()` - Debugging async code
- `WriteTo.Debug()` - Only in DEBUG builds (conditional compilation)
- `WriteTo.Async()` - File sink with daily rolling and 7-day retention

**Usage in Project**:
```csharp
Log.Information("Application started");
Log.Debug("Refreshing process snapshot");
Log.Warning("Failed to open process {Pid}", pid);
Log.Error(ex, "Critical error in update loop");
```

**Files Using This**:
- `App.xaml.cs` (initialization)
- `Services/ProcessService.cs`
- `ViewModels/MainViewModel.cs`

---

### Serilog.Enrichers.Process

**Version**: 3.0.0  
**NuGet**: https://www.nuget.org/packages/Serilog.Enrichers.Process/  
**License**: Apache-2.0  
**Purpose**: Adds process ID to log entries

**Usage**:
```csharp
.Enrich.WithProcessId()
```

**Benefit**: Helps distinguish log entries in multi-instance scenarios.

---

### Serilog.Enrichers.Thread

**Version**: 4.0.0  
**NuGet**: https://www.nuget.org/packages/Serilog.Enrichers.Thread/  
**License**: Apache-2.0  
**Purpose**: Adds thread ID to log entries

**Usage**:
```csharp
.Enrich.WithThreadId()
```

**Benefit**: Essential for debugging threading issues in async code.

---

### Serilog.Sinks.Async

**Version**: 2.1.0  
**NuGet**: https://www.nuget.org/packages/Serilog.Sinks.Async/  
**License**: Apache-2.0  
**Purpose**: Asynchronous logging to prevent I/O blocking

**Usage**:
```csharp
.WriteTo.Async(a => a.File(...))
```

**Benefit**: 
- Logging happens on background thread
- Main thread never blocks on disk I/O
- Critical for maintaining UI responsiveness

---

### Serilog.Sinks.Debug

**Version**: 3.0.0  
**NuGet**: https://www.nuget.org/packages/Serilog.Sinks.Debug/  
**License**: Apache-2.0  
**Purpose**: Writes log events to debugger output window

**Usage**:
Enabled only in DEBUG builds using conditional compilation:
```csharp
#if DEBUG
loggerConfiguration.WriteTo.Debug();
#endif
```

**Benefit**: 
- Zero overhead in Release builds (completely excluded)
- Useful for development debugging in Visual Studio Output window
- No file I/O during development

**Files Using This**:
- `App.xaml.cs` (conditional compilation)

---

### Serilog.Sinks.File

**Version**: 7.0.0  
**NuGet**: https://www.nuget.org/packages/Serilog.Sinks.File/  
**License**: Apache-2.0  
**Purpose**: File-based logging sink

**Configuration Options**:
```csharp
.WriteTo.File(
    path: "logs/SystemProcesses-.log",
    rollingInterval: RollingInterval.Day,  // New file daily
    retainedFileCountLimit: 7,             // Keep last 7 days
    fileSizeLimitBytes: 10_000_000,        // 10 MB max per file
    rollOnFileSizeLimit: true)
```

**Log Location**: `logs/SystemProcesses-YYYYMMDD.log`

---

## Performance & Utilities

### Microsoft.Extensions.ObjectPool

**Version**: 10.0.1  
**NuGet**: https://www.nuget.org/packages/Microsoft.Extensions.ObjectPool/  
**License**: MIT  
**Purpose**: High-performance object pooling

**Why This Package**:
- Thread-safe object pooling
- Zero-allocation rent/return operations
- Configurable pool policies
- Official Microsoft package

**Usage in Project**:
```csharp
// StringBuilderPool.cs
private static readonly ObjectPool<StringBuilder> sbPool;

static StringBuilderPool()
{
    var policy = new StringBuilderPooledObjectPolicy(256, 65536);
    sbPool = new DefaultObjectPool<StringBuilder>(policy, maxRetained: 32);
}

public static PooledStringBuilder Rent()
{
    return new PooledStringBuilder(sbPool.Get(), sbPool);
}
```

**Performance Impact**:
- Reduced Gen0 collections by 50%
- Eliminated 120 KB/sec string allocation rate
- Critical for hot path string formatting

**Files Using This**:
- `Helpers/StringBuilderPool.cs`
- `ViewModels/ProcessItemViewModel.cs` (via StringBuilderPool)

---

## Windows-Specific Dependencies

### System.Drawing.Common

**Version**: 10.0.1  
**NuGet**: https://www.nuget.org/packages/System.Drawing.Common/  
**License**: MIT  
**Purpose**: GDI+ icon extraction

**Why This Package**:
- Required for `Icon.ExtractAssociatedIcon()`
- Extracts icons from executables
- No WPF-native alternative

**Usage in Project**:
```csharp
// IconCache.cs
using System.Drawing;

public static BitmapSource? ExtractIcon(string executablePath)
{
    using Icon? icon = Icon.ExtractAssociatedIcon(executablePath);
    if (icon == null) return null;
    
    // Convert to WPF BitmapSource
    var bitmap = Imaging.CreateBitmapSourceFromHIcon(
        icon.Handle,
        Int32Rect.Empty,
        BitmapSizeOptions.FromEmptyOptions());
    
    bitmap.Freeze();
    return bitmap;
}
```

**Security Note**: On .NET 6+, this package only works on Windows (by design).

**Files Using This**:
- `Services/IconCache.cs`

---

### System.Management

**Version**: 10.0.1  
**NuGet**: https://www.nuget.org/packages/System.Management/  
**License**: MIT  
**Purpose**: WMI (Windows Management Instrumentation) access

**Current Usage**: Minimal; kept for potential future features.

**Potential Use Cases**:
- Query additional process properties
- Monitor process creation events
- Access performance counters

**Note**: Currently not heavily used. Consider removing if not needed in future versions.

---

### System.ServiceProcess.ServiceController

**Version**: 10.0.1  
**NuGet**: https://www.nuget.org/packages/System.ServiceProcess.ServiceController/  
**License**: MIT  
**Purpose**: Windows service management

**Usage in Project**:
```csharp
// Alternative to direct P/Invoke for service enumeration
// Currently, we use advapi32.dll EnumServicesStatusExW instead
```

**Status**: Referenced but not actively used. Direct P/Invoke provides better performance.

---

## Testing Dependencies

### NUnit

**Version**: 4.3.2  
**NuGet**: https://www.nuget.org/packages/NUnit/  
**License**: MIT  
**Purpose**: Unit testing framework

**Why NUnit**:
- Modern, actively maintained testing framework
- Excellent assertion syntax with fluent API
- Strong community support and documentation
- Better than MSTest for desktop applications
- Supports async test methods natively

**Usage in Project**:
```csharp
[TestFixture]
public class ProcessServiceTests
{
    [SetUp]
    public void Setup() { }
    
    [Test]
    public void ProcessService_WhenInitialized_ShouldNotAllocate()
    {
        // Test implementation
    }
}
```

**Files Using This**:
- `SystemProcesses.Tests/ProcessServiceTests.cs`

**Migration Note**: Converted from MSTest to NUnit in January 2026. See `learnings.md` for decision rationale.

---

### NUnit.Analyzers

**Version**: 4.7.0  
**NuGet**: https://www.nuget.org/packages/NUnit.Analyzers/  
**License**: MIT  
**Purpose**: Roslyn analyzers for NUnit best practices

**Benefits**:
- Compile-time warnings for common NUnit mistakes
- Suggests proper assertion syntax
- Detects incorrect test method signatures
- Enforces naming conventions

**Configuration**: Automatically enabled when NUnit is referenced.

---

### NUnit3TestAdapter

**Version**: 5.0.0  
**NuGet**: https://www.nuget.org/packages/NUnit3TestAdapter/  
**License**: MIT  
**Purpose**: Visual Studio test explorer integration

**Benefits**:
- Runs NUnit tests from Visual Studio Test Explorer
- Supports debugging tests with breakpoints
- Integrates with CI/CD pipelines
- Provides test result reporting

**Usage**: Automatic; no code changes required.

---

### coverlet.collector

**Version**: 6.0.4  
**NuGet**: https://www.nuget.org/packages/coverlet.collector/  
**License**: MIT  
**Purpose**: Code coverage collection for unit tests

**Why This Package**:
- Measures which code paths are exercised by tests
- Generates coverage reports (OpenCover, Cobertura formats)
- Integrates with CI/CD for coverage gates
- Zero-overhead in production builds

**Usage**:
```bash
# Run tests with coverage collection
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

**Configuration**: Enabled via project properties in `.csproj`:
```xml
<ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
</ItemGroup>
```

**Target**: Aim for >80% code coverage on critical paths (ProcessService, differential update algorithm).

---

### Moq

**Version**: 4.20.72  
**NuGet**: https://www.nuget.org/packages/Moq/  
**License**: BSD-3-Clause  
**Purpose**: Mocking framework for unit tests

**Why This Package**:
- Creates mock objects for dependency injection in tests
- Verifies method calls and argument values
- Simulates complex behaviors (exceptions, delays)
- Reduces test complexity by isolating units under test

**Usage Example**:
```csharp
[Test]
public void ProcessService_WhenImageLoaderFails_ShouldContinue()
{
    // Arrange
    var mockImageLoader = new Mock<IImageLoaderService>();
    mockImageLoader
        .Setup(x => x.LoadIconAsync(It.IsAny<string>()))
        .ThrowsAsync(new IOException("Network error"));
    
    var service = new ProcessService(mockImageLoader.Object);
    
    // Act & Assert
    Assert.DoesNotThrowAsync(() => service.RefreshAsync());
}
```

**Files Using This**:
- `SystemProcesses.Tests/ProcessServiceTests.cs` (future expansion)

---

## Dependency Update Policy

### Update Frequency

- **Major Version Updates**: Review every 6 months or when significant features are needed
- **Minor/Patch Updates**: Monthly security and bug fix updates
- **Framework Updates**: Follow .NET release cadence (annually)

### Update Process

1. **Check Release Notes**: Review breaking changes and new features
2. **Update Test Environment**: Test in isolated branch
3. **Run Benchmarks**: Verify no performance regressions
4. **Test All Features**: Full regression testing
5. **Update Documentation**: Note any API changes

### Compatibility Matrix

| Package | Min .NET Version | Platform |
|---------|------------------|----------|
| CommunityToolkit.Mvvm | .NET 6+ | Any |
| H.NotifyIcon.Wpf | .NET 6+ | Windows |
| Serilog | .NET 6+ | Any |
| System.Drawing.Common | .NET 6+ | Windows |
| Microsoft.Extensions.ObjectPool | .NET 6+ | Any |

---

## Security Considerations

### NuGet Package Security

1. **Verify Package Sources**: All packages from official nuget.org
2. **Check Package Signatures**: Verify Microsoft/Community Toolkit signatures
3. **Review Dependencies**: Monitor for CVE announcements
4. **Lock File**: Consider using `packages.lock.json` for reproducible builds

### Vulnerability Monitoring

```bash
# Check for known vulnerabilities
dotnet list package --vulnerable

# Audit dependencies
dotnet list package --include-transitive
```

---

## Build Configuration

### Package References in .csproj

**Production Project** (`SystemProcesses.Desktop.csproj`):
```xml
<ItemGroup>
    <!-- MVVM Framework -->
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageReference Include="CommunityToolkit.HighPerformance" Version="8.4.0" />
    
    <!-- System Tray -->
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.3.2" />
    
    <!-- Performance -->
    <PackageReference Include="Microsoft.Extensions.ObjectPool" Version="10.0.1" />
    
    <!-- Logging -->
    <PackageReference Include="Serilog" Version="4.3.0" />
    <PackageReference Include="Serilog.Enrichers.Process" Version="3.0.0" />
    <PackageReference Include="Serilog.Enrichers.Thread" Version="4.0.0" />
    <PackageReference Include="Serilog.Sinks.Async" Version="2.1.0" />
    <PackageReference Include="Serilog.Sinks.Debug" Version="3.0.0" />
    <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
    
    <!-- Windows Integration -->
    <PackageReference Include="System.Drawing.Common" Version="10.0.1" />
    <PackageReference Include="System.Management" Version="10.0.1" />
    <PackageReference Include="System.ServiceProcess.ServiceController" Version="10.0.1" />
</ItemGroup>
```

**Test Project** (`SystemProcesses.Tests.csproj`):
```xml
<ItemGroup>
    <!-- Testing Framework -->
    <PackageReference Include="NUnit" Version="4.3.2" />
    <PackageReference Include="NUnit.Analyzers" Version="4.7.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="5.0.0" />
    
    <!-- Code Coverage -->
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    
    <!-- Mocking -->
    <PackageReference Include="Moq" Version="4.20.72" />
    
    <!-- Build Tools -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
</ItemGroup>

<ItemGroup>
    <Using Include="NUnit.Framework" />
</ItemGroup>
```

---

## Transitive Dependencies

### Automatically Included

The packages above bring in additional transitive dependencies:

- **Microsoft.Extensions.DependencyInjection.Abstractions** (via ObjectPool)
- **System.Text.Json** (via CommunityToolkit)
- **System.ComponentModel.Annotations** (via CommunityToolkit.Mvvm)

### View Full Dependency Tree

```bash
dotnet list package --include-transitive
```

---

## Optional Dependencies (Not Used)

### Considered but Rejected

**xUnit / NUnit**: Testing frameworks
- **Status**: Not currently included
- **Reason**: Project focuses on production code; tests would be in separate project

**BenchmarkDotNet**: Performance benchmarking
- **Status**: Not included in production build
- **Reason**: Only needed during development; not shipped

**Polly**: Resilience and transient-fault-handling
- **Status**: Not needed
- **Reason**: All operations are local; no network calls requiring retry logic

---

## Troubleshooting

### Common Issues

#### Issue: Package Restore Fails

```bash
# Clear NuGet cache
dotnet nuget locals all --clear

# Restore packages
dotnet restore
```

#### Issue: Version Conflicts

```bash
# Check for version mismatches
dotnet list package --outdated
```

#### Issue: Missing Package at Runtime

**Symptom**: `FileNotFoundException` for DLL

**Solution**: Ensure `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` if needed

---

## Future Dependencies Under Consideration

### Potential Additions

1. **Microsoft.Diagnostics.NETCore.Client**
   - Purpose: Advanced process diagnostics
   - Use Case: Dump collection, profiling

2. **CliWrap**
   - Purpose: Wrapper for command-line processes
   - Use Case: Launching external tools

3. **Avalonia UI**
   - Purpose: Cross-platform UI framework
   - Use Case: If cross-platform support becomes required

---

## References

- **NuGet Gallery**: https://www.nuget.org/
- **CommunityToolkit Docs**: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/
- **Serilog Wiki**: https://github.com/serilog/serilog/wiki
- **.NET API Browser**: https://learn.microsoft.com/en-us/dotnet/api/