# Authoritative References & URLs

This document contains curated links to official documentation, community resources, tools, and references used in the SystemProcesses project.

---

## Table of Contents

1. [Official Microsoft Documentation](#official-microsoft-documentation)
2. [.NET & C# Language](#net--c-language)
3. [Windows API & Native Programming](#windows-api--native-programming)
4. [WPF & XAML](#wpf--xaml)
5. [NuGet Packages](#nuget-packages)
6. [Performance & Profiling](#performance--profiling)
7. [Security Resources](#security-resources)
8. [Community Projects & Tools](#community-projects--tools)
9. [Learning Resources](#learning-resources)
10. [Development Tools](#development-tools)

---

## Official Microsoft Documentation

### .NET Platform

- **.NET Documentation Home**  
  https://learn.microsoft.com/en-us/dotnet/

- **.NET 9 Release Notes**  
  https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9

- **.NET API Browser**  
  https://learn.microsoft.com/en-us/dotnet/api/

- **C# Language Reference**  
  https://learn.microsoft.com/en-us/dotnet/csharp/

- **Common Language Runtime (CLR)**  
  https://learn.microsoft.com/en-us/dotnet/standard/clr

### Framework Design Guidelines

- **Framework Design Guidelines Overview**  
  https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/

- **Naming Guidelines**  
  https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/naming-guidelines

- **Dispose Pattern**  
  https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose

---

## .NET & C# Language

### Performance

- **Performance Best Practices**  
  https://learn.microsoft.com/en-us/dotnet/framework/performance/

- **High-Performance .NET Code**  
  https://github.com/dotnet/performance

- **Span<T> and Memory<T> Usage**  
  https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/

- **stackalloc Documentation**  
  https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/stackalloc

### Unsafe Code & Pointers

- **Unsafe Code and Pointers**  
  https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code

- **Pointer Types**  
  https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code#pointer-types

### Async Programming

- **Async/Await Best Practices**  
  https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming

- **Task-based Asynchronous Pattern**  
  https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap

### Interop

- **Platform Invoke (P/Invoke)**  
  https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke

- **LibraryImport Attribute (.NET 7+)**  
  https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation

- **Marshalling Data**  
  https://learn.microsoft.com/en-us/dotnet/standard/native-interop/type-marshalling

- **SafeHandle Class**  
  https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle

---

## Windows API & Native Programming

### Official Windows API Documentation

- **Windows API Index**  
  https://learn.microsoft.com/en-us/windows/win32/api/

- **Process and Thread Functions**  
  https://learn.microsoft.com/en-us/windows/win32/procthread/process-and-thread-functions

- **Memory Management Functions**  
  https://learn.microsoft.com/en-us/windows/win32/memory/memory-management-functions

- **Performance Counters**  
  https://learn.microsoft.com/en-us/windows/win32/perfctrs/performance-counters-portal

### Native API (Undocumented)

- **NT Documentation Project**  
  https://ntdoc.m417z.com/

- **NTAPI Undocumented Functions**  
  http://undocumented.ntinternals.net/

- **ReactOS Documentation**  
  https://doxygen.reactos.org/

### Specific APIs

- **NtQuerySystemInformation**  
  https://ntdoc.m417z.com/ntquerysysteminformation.html

- **OpenProcess Function**  
  https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess

- **GlobalMemoryStatusEx**  
  https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex

- **PDH Functions**  
  https://learn.microsoft.com/en-us/windows/win32/perfctrs/using-the-pdh-functions-to-consume-counter-data

---

## WPF & XAML

### WPF Documentation

- **WPF Overview**  
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/

- **Data Binding Overview**  
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/

- **MVVM Pattern in WPF**  
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/data-binding-overview

### Performance

- **Optimizing WPF Application Performance**  
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-wpf-application-performance

- **UI Virtualization**  
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-improve-the-scrolling-performance-of-a-listbox

- **Freezable Objects**  
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/freezable-objects-overview

### TreeView

- **TreeView Overview**  
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/treeview-overview

- **VirtualizingStackPanel**  
  https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.virtualizingstackpanel

---

## NuGet Packages

### CommunityToolkit

- **CommunityToolkit.Mvvm**  
  https://www.nuget.org/packages/CommunityToolkit.Mvvm/  
  https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/

- **CommunityToolkit.HighPerformance**  
  https://www.nuget.org/packages/CommunityToolkit.HighPerformance/  
  https://learn.microsoft.com/en-us/dotnet/communitytoolkit/high-performance/introduction

### Logging

- **Serilog**  
  https://www.nuget.org/packages/Serilog/  
  https://serilog.net/  
  https://github.com/serilog/serilog/wiki

- **Serilog.Sinks.File**  
  https://www.nuget.org/packages/Serilog.Sinks.File/  
  https://github.com/serilog/serilog-sinks-file

- **Serilog.Sinks.Async**  
  https://www.nuget.org/packages/Serilog.Sinks.Async/  
  https://github.com/serilog/serilog-sinks-async

### System Tray

- **H.NotifyIcon.Wpf**  
  https://www.nuget.org/packages/H.NotifyIcon.Wpf/  
  https://github.com/HavenDV/H.NotifyIcon

### Performance

- **Microsoft.Extensions.ObjectPool**  
  https://www.nuget.org/packages/Microsoft.Extensions.ObjectPool/  
  https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.objectpool

---

## Performance & Profiling

### Tools

- **BenchmarkDotNet**  
  https://benchmarkdotnet.org/  
  https://github.com/dotnet/BenchmarkDotNet

- **PerfView**  
  https://github.com/microsoft/perfview  
  https://learn.microsoft.com/en-us/shows/perfview-tutorial/

- **dotMemory (JetBrains)**  
  https://www.jetbrains.com/dotmemory/

- **dotTrace (JetBrains)**  
  https://www.jetbrains.com/dottrace/

- **Visual Studio Profiler**  
  https://learn.microsoft.com/en-us/visualstudio/profiling/

### Guides

- **Performance Profiling in .NET**  
  https://learn.microsoft.com/en-us/dotnet/core/diagnostics/

- **Memory Leak Detection**  
  https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-memory-leak

- **High-Performance Coding Patterns**  
  https://github.com/dotnet/performance/blob/main/docs/coding-guidelines.md

---

## Security Resources

### OWASP

- **OWASP Top 10**  
  https://owasp.org/www-project-top-ten/

- **OWASP Desktop Security**  
  https://owasp.org/www-community/vulnerabilities/

### Microsoft Security

- **Secure Coding Guidelines**  
  https://learn.microsoft.com/en-us/dotnet/standard/security/secure-coding-guidelines

- **Code Access Security**  
  https://learn.microsoft.com/en-us/dotnet/framework/misc/code-access-security

- **Security Advisories**  
  https://github.com/dotnet/announcements/issues?q=is%3Aopen+is%3Aissue+label%3ASecurity

### Vulnerability Scanning

- **dotnet list package --vulnerable**  
  https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-list-package

- **Snyk for .NET**  
  https://snyk.io/product/open-source-security-management/

---

## Community Projects & Tools

### Reference Implementations

- **Process Hacker**  
  https://github.com/processhacker/processhacker  
  *(Excellent reference for native API usage)*

- **System Informer (formerly Process Hacker)**  
  https://systeminformer.sourceforge.io/  
  https://github.com/winsiderss/systeminformer

- **Process Explorer (Sysinternals)**  
  https://learn.microsoft.com/en-us/sysinternals/downloads/process-explorer

### P/Invoke Resources

- **pinvoke.net**  
  http://www.pinvoke.net/  
  *(Community-contributed P/Invoke signatures)*

- **CsWin32**  
  https://github.com/microsoft/CsWin32  
  *(Source generator for Windows API P/Invoke)*

---

## Learning Resources

### Books

- **CLR via C# (Jeffrey Richter)**  
  https://www.microsoftpressstore.com/store/clr-via-c-sharp-9780735667457

- **Pro .NET Memory Management (Konrad Kokosa)**  
  https://prodotnetmemory.com/

- **Windows Internals (Russinovich, Solomon, Ionescu)**  
  https://www.microsoftpressstore.com/store/windows-internals-part-1-9780735684188

### Articles & Blogs

- **.NET Blog**  
  https://devblogs.microsoft.com/dotnet/

- **Stephen Toub's Performance Posts**  
  https://devblogs.microsoft.com/dotnet/author/toub/

- **Adam Sitnik's Blog (BenchmarkDotNet author)**  
  https://adamsitnik.com/

### Video Courses

- **Microsoft Learn: .NET**  
  https://learn.microsoft.com/en-us/training/dotnet/

- **Channel 9: .NET Videos**  
  https://learn.microsoft.com/en-us/shows/

---

## Development Tools

### IDEs & Editors

- **Visual Studio 2022**  
  https://visualstudio.microsoft.com/

- **JetBrains Rider**  
  https://www.jetbrains.com/rider/

- **Visual Studio Code**  
  https://code.visualstudio.com/

### Build & CI/CD

- **MSBuild Documentation**  
  https://learn.microsoft.com/en-us/visualstudio/msbuild/

- **GitHub Actions for .NET**  
  https://github.com/actions/setup-dotnet

- **Azure DevOps**  
  https://azure.microsoft.com/en-us/products/devops/

### Debugging

- **WinDbg**  
  https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/

- **dotnet-dump**  
  https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump

- **dotnet-trace**  
  https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace

### Code Analysis

- **Roslyn Analyzers**  
  https://github.com/dotnet/roslyn-analyzers

- **SonarQube for .NET**  
  https://www.sonarsource.com/products/sonarqube/

- **StyleCop**  
  https://github.com/DotNetAnalyzers/StyleCopAnalyzers

---

## Additional Resources

### GitHub Repositories

- **.NET Runtime Source**  
  https://github.com/dotnet/runtime  
  *(See actual BCL implementation)*

- **.NET Performance Repository**  
  https://github.com/dotnet/performance

- **Awesome .NET**  
  https://github.com/quozd/awesome-dotnet  
  *(Curated list of .NET libraries and tools)*

### Stack Overflow Tags

- **[.net]**  
  https://stackoverflow.com/questions/tagged/.net

- **[c#]**  
  https://stackoverflow.com/questions/tagged/c%23

- **[wpf]**  
  https://stackoverflow.com/questions/tagged/wpf

- **[pinvoke]**  
  https://stackoverflow.com/questions/tagged/pinvoke

### Reddit Communities

- **r/csharp**  
  https://www.reddit.com/r/csharp/

- **r/dotnet**  
  https://www.reddit.com/r/dotnet/

---

## Monitoring & Updates

### Stay Updated

- **.NET Release Notes**  
  https://github.com/dotnet/core/tree/main/release-notes

- **.NET Announcements**  
  https://github.com/dotnet/announcements

- **Security Advisories**  
  https://github.com/dotnet/announcements/labels/Security

### NuGet Package Updates

- **NuGet Gallery**  
  https://www.nuget.org/

- **NuGet Package Explorer**  
  https://github.com/NuGetPackageExplorer/NuGetPackageExplorer

---

## Quick Reference Links

| Category | Link |
|----------|------|
| .NET Docs | https://learn.microsoft.com/en-us/dotnet/ |
| Windows API | https://learn.microsoft.com/en-us/windows/win32/api/ |
| Native API | https://ntdoc.m417z.com/ |
| WPF Docs | https://learn.microsoft.com/en-us/dotnet/desktop/wpf/ |
| Serilog | https://serilog.net/ |
| BenchmarkDotNet | https://benchmarkdotnet.org/ |
| Process Hacker | https://github.com/processhacker/processhacker |
| pinvoke.net | http://www.pinvoke.net/ |
| Stack Overflow | https://stackoverflow.com/ |

---

## Notes

- **Bookmark Priority**: URLs marked with ✨ are essential references
- **Version-Specific**: Some links may reference .NET 9; adjust for your framework version
- **Community Resources**: While helpful, always validate against official documentation
- **Dead Links**: If a link is broken, check the Internet Archive (https://archive.org/)

---

**Last Updated**: 2025  
**Maintained By**: SystemProcesses Development Team
