# LLM Context Engineering Documentation

This directory contains comprehensive documentation specifically designed to provide AI Language Models (LLMs) with deep context about the SystemProcesses project. This approach, known as **Context Engineering**, enables LLMs to provide accurate, contextually-aware assistance when working on this codebase.

---

## 📋 What is Context Engineering?

Context Engineering is the practice of providing LLMs with all necessary information to accomplish tasks within their context window. Instead of relying on the model's general knowledge, we provide:

- **Explicit Documentation**: Architecture decisions, patterns, and constraints
- **Historical Context**: Why decisions were made, what alternatives were considered
- **Style Guides**: Coding standards, naming conventions, and anti-patterns
- **Examples**: Practical code snippets demonstrating project-specific patterns
- **Domain Vocabulary**: Glossary of project-specific terms and concepts

This approach results in:
- ✅ More accurate code suggestions
- ✅ Consistency with project architecture
- ✅ Awareness of performance constraints and optimization goals
- ✅ Understanding of project-specific patterns (e.g., Zero-Allocation Architecture)

---

## 📚 Documentation Files

### 1. `architecture.md` - System Architecture
**Purpose**: High-level system design and architectural patterns

**Contains**:
- Layered architecture diagram (Services → ViewModels → Views)
- Data flow pipeline from Windows Kernel to WPF UI
- Threading model (Producer-Consumer pattern)
- Key design patterns (MVVM, Object Pooling, Differential Updates)
- Memory management strategy
- Performance characteristics and benchmarks

**Use When**: Understanding system structure, adding new components, or refactoring architecture

---

### 2. `learnings.md` - Technical Decisions & Rationale
**Purpose**: Historical record of architectural decisions and lessons learned

**Contains**:
- Decision logs with context, rationale, and trade-offs
- Performance optimization discoveries
- Mistakes made and how they were corrected
- Testing insights and profiling results
- Future recommendations

**Use When**: Understanding WHY the code works this way, evaluating alternatives, or avoiding past mistakes

**Key Decisions Documented**:
- Why direct kernel API access vs `System.Diagnostics`
- Why manual buffer management vs managed arrays
- Why `CommunityToolkit.Mvvm` source generators
- Why differential UI updates vs full rebuild

---

### 3. `coding-standards.md` - Code Conventions
**Purpose**: Definitive guide to code style, patterns, and requirements

**Contains**:
- Naming conventions (PascalCase, camelCase rules)
- File organization and structure
- Unsafe code standards and safety rules
- Performance rules (no LINQ in hot paths, object pooling)
- MVVM pattern standards
- P/Invoke standards
- WPF-specific patterns

**Use When**: Writing new code, reviewing code, or ensuring consistency

**Critical Rules**:
- ❌ No LINQ in hot paths (refresh loops)
- ❌ No unvalidated pointer operations
- ✅ Always freeze WPF objects for cross-thread use
- ✅ Use `LibraryImport` for P/Invoke (.NET 7+)
- ✅ Implement `IDisposable` for unmanaged resources

---

### 4. `api-reference.md` - API Documentation
**Purpose**: Reference for Windows Native APIs and project interfaces

**Contains**:
- Windows API signatures and usage patterns
  - `NtQuerySystemInformation` (ntdll.dll)
  - `OpenProcess`, `GlobalMemoryStatusEx` (kernel32.dll)
  - PDH performance counter APIs
- Project service interfaces (`IProcessService`, `IImageLoaderService`)
- Data structures (`ProcessInfo`, `SystemStats`, `UnicodeString`)
- Helper classes (`StringBuilderPool`, `IconCache`)
- Performance characteristics and error handling patterns

**Use When**: Calling Windows APIs, implementing services, or understanding data structures

---

### 5. `dependencies.md` - NuGet Packages
**Purpose**: Complete inventory of external dependencies

**Contains**:
- All NuGet packages with versions and purposes
- Why each package was chosen
- Usage patterns and configuration
- Alternatives considered and rejected
- Update policy and security considerations

**Use When**: Adding dependencies, updating packages, or understanding build requirements

**Key Packages**:
- `CommunityToolkit.Mvvm` 8.4.0 - MVVM source generators
- `Serilog` 4.3.0 - Structured logging
- `H.NotifyIcon.Wpf` 2.3.2 - System tray integration
- `Microsoft.Extensions.ObjectPool` 10.0.1 - Object pooling

---

### 6. `examples.md` - Code Patterns & Snippets
**Purpose**: Practical, copy-paste-ready examples of project patterns

**Contains**:
- Zero-allocation patterns (reusable collections, stack allocation)
- Object pooling usage (`StringBuilderPool`)
- MVVM implementation with source generators
- P/Invoke and unsafe code patterns
- WPF-specific patterns (freezing, dispatcher, ObservableCollection)
- Threading and async patterns
- Performance optimization examples

**Use When**: Implementing similar functionality, learning project patterns, or refactoring code

**Example Categories**:
1. Zero-Allocation Patterns
2. Object Pooling
3. MVVM Implementation
4. P/Invoke & Native API Usage
5. Unsafe Code Patterns
6. WPF-Specific Patterns
7. Threading & Async Patterns
8. Performance Optimization

---

### 7. `glossary.md` - Project Terminology
**Purpose**: Dictionary of project-specific terms and acronyms

**Contains**:
- Alphabetical listing of all project-specific terms
- Definitions with context and usage
- Acronym reference table
- Related terms grouped by category

**Use When**: Encountering unfamiliar terms, writing documentation, or onboarding

**Key Terms**:
- Zero-Allocation, Hot Path, Differential Update
- PID, Parent PID, Process Identity
- Native API, P/Invoke, LibraryImport
- Freezing, Virtualization, Dispatcher
- ProcessInfo, SystemStats, UnicodeString

---

### 8. `urls.md` - Authoritative References
**Purpose**: Curated links to official documentation and resources

**Contains**:
- Microsoft official documentation
- Windows API references
- Community projects (Process Hacker, System Informer)
- Performance tools (BenchmarkDotNet, PerfView)
- Security resources (OWASP)
- Learning resources (books, blogs, courses)

**Use When**: Verifying API signatures, researching patterns, or learning new concepts

---

## 🎯 How to Use This Documentation

### For LLMs (AI Assistants)

When working on this project, LLMs should:

1. **Read `architecture.md` first** to understand system structure
2. **Consult `coding-standards.md`** before generating code
3. **Reference `examples.md`** for project-specific patterns
4. **Use `glossary.md`** to understand terminology
5. **Check `learnings.md`** to avoid past mistakes
6. **Verify APIs** against `api-reference.md`

### For Human Developers

When contributing to this project:

1. **Onboarding**: Read `architecture.md` → `learnings.md` → `coding-standards.md`
2. **Writing Code**: Use `examples.md` as reference, follow `coding-standards.md`
3. **Adding Features**: Check `architecture.md` for integration points
4. **Fixing Bugs**: Consult `learnings.md` for common issues
5. **Code Review**: Use `coding-standards.md` checklist

---

## 🔍 Quick Navigation

| Need to... | Read this file |
|------------|----------------|
| Understand system architecture | `architecture.md` |
| Learn why decisions were made | `learnings.md` |
| Write code that matches project style | `coding-standards.md` |
| Call a Windows API | `api-reference.md` |
| Add/update NuGet packages | `dependencies.md` |
| See code examples | `examples.md` |
| Look up a term | `glossary.md` |
| Find official docs | `urls.md` |

---

## 🏗️ Project Context Summary

**SystemProcesses** is a high-performance, zero-allocation Windows system monitor built with .NET 9 and WPF. Includes an optional always-on-top StatsView overlay for real-time system monitoring. Key characteristics:

### Core Philosophy
- **Zero-Allocation Architecture**: Minimize GC pressure through object reuse, pooling, and stack allocation
- **Performance First**: Direct kernel API access, unsafe pointer operations, manual memory management
- **MVVM Pattern**: Clean separation of UI, presentation logic, and business logic

### Technical Constraints
- **Platform**: Windows 10/11 only (x64 recommended)
- **Framework**: .NET 9.0
- **Language**: C# 12 with unsafe code
- **Architecture**: WPF desktop application

### Performance Goals
- Full system snapshot: <5ms
- UI refresh cycle: <2ms
- Memory footprint: <30MB for 300 processes
- Zero allocations per refresh after warmup

### Key Technologies
- **Native APIs**: `ntdll.dll` (NtQuerySystemInformation), `kernel32.dll`, `advapi32.dll`, `pdh.dll`
- **MVVM Framework**: CommunityToolkit.Mvvm with source generators
- **Logging**: Serilog with async file sink
- **Pooling**: Microsoft.Extensions.ObjectPool

---

## 📖 Documentation Principles

This documentation follows these principles:

1. **Accuracy**: All information is verified and up-to-date
2. **Completeness**: Covers architecture, decisions, standards, and examples
3. **Searchability**: Well-organized with clear section headings
4. **Practicality**: Includes working code examples
5. **Context**: Explains WHY, not just WHAT or HOW

---

## 🔄 Maintenance

### Keeping Documentation Current

When making significant changes to the project:

1. **Architecture Changes**: Update `architecture.md`
2. **New Decisions**: Document in `learnings.md` with rationale
3. **New Patterns**: Add examples to `examples.md`
4. **New Terms**: Define in `glossary.md`
5. **New Dependencies**: Document in `dependencies.md`
6. **Code Style Changes**: Update `coding-standards.md`

### Review Cycle

- **Minor Updates**: As needed when code changes
- **Major Review**: Every 6 months or major version bump
- **Accuracy Check**: Before major releases

---

## 🎓 Learning Path

### For New Contributors

**Week 1 - Understanding**:
1. Read `architecture.md` (2 hours)
2. Read `learnings.md` (1 hour)
3. Skim `coding-standards.md` (30 min)

**Week 2 - Exploration**:
1. Run the application
2. Read `ProcessService.cs` with `api-reference.md` open
3. Study `examples.md` patterns

**Week 3 - Contributing**:
1. Pick a small task
2. Follow `coding-standards.md`
3. Reference `examples.md` for patterns

---

## 🤝 Contributing to Documentation

When improving these docs:

- **Be Precise**: Use specific examples and measurements
- **Cite Sources**: Link to official docs in `urls.md`
- **Show Code**: Include working examples
- **Explain Rationale**: Don't just state facts, explain WHY
- **Keep Updated**: Update related docs when making changes

---

## 📞 Contact & Support

For questions about this documentation:
- Open an issue on GitHub
- Check existing discussions
- Consult `urls.md` for authoritative external resources

---

**Documentation Version**: 1.0  
**Last Updated**: 2025  
**Compatible With**: SystemProcesses v1.x (.NET 9)

---

## Appendix: File Sizes

| File | Lines | Purpose |
|------|---------|---------|
| `architecture.md` | ~600 | System design & patterns |
| `learnings.md` | ~1900 | Decisions & lessons (updated Jan 2026) |
| `coding-standards.md` | ~1200 | Code style & conventions (updated Jan 2026) |
| `api-reference.md` | ~900 | API documentation |
| `dependencies.md` | ~750 | NuGet packages (updated Jan 2026) |
| `examples.md` | ~1400 | Code patterns (updated Jan 2026) |
| `glossary.md` | ~580 | Terminology |
| `urls.md` | ~480 | External references |
| **Total** | **~8410** | **Complete context** |

This comprehensive documentation set provides LLMs with ~8400 lines of curated, project-specific context.

---

## Recent Updates (January 2026)

### Critical Fixes Documented
- **C1: Comprehensive Unit Testing Framework** - NUnit test suite with 8 critical path tests
- **C2: Unsafe Code Validation** - Buffer bounds checking, pointer arithmetic validation, string encoding validation
- **H1: PDH Error Handling** - Detailed logging for performance counter initialization
- **H3: Thread-Safe Caching** - ConcurrentDictionary for ViewModel cache
- **M1: Magic Numbers Extraction** - Named constants for configuration values

### Documentation Enhancements
- `learnings.md`: Added sections 10-13 documenting recent fixes and decisions
- `coding-standards.md`: Added sections 9-15 with validation patterns, error handling, thread-safety, and testing guidelines
- `examples.md`: Added 8 new validation and error handling examples
- `dependencies.md`: Added testing packages (NUnit, coverlet, Moq) with detailed documentation

### New Steering Document
- `.kiro/steering/patterns.md` - Critical coding patterns for AI assistants
