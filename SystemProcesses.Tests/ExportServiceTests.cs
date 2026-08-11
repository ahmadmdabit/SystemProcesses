using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using SystemProcesses.Desktop.Models;
using SystemProcesses.Desktop.Services;
using SystemProcesses.Desktop.ViewModels;

namespace SystemProcesses.Tests;

/// <summary>
/// Unit tests for the process-snapshot export pipeline: snapshot deep-copy guarantees,
/// CSV / JSON / Markdown rendering, and the export service's path handling.
/// </summary>
[TestFixture]
public class ExportServiceTests
{
    private static List<ProcessInfo> BuildSampleSnapshot()
    {
        var root = new ProcessInfo
        {
            Pid = 100,
            ParentPid = 0,
            Name = "Base.exe",
            CpuPercentage = 5.5,
            MemoryBytes = (long)2.048e6,
            VirtualMemoryBytes = (long)2.1e6,
            ThreadCount = 3,
            HandleCount = 40,
            IsService = true,
            ProcessPath = @"C:\Program Files\Base\Base.exe",
            Parameters = "-arg1 value,\"quoted\"",
            CreateTime = (long)1.32e17 // ~2019-01-04 UTC
        };

        var child = new ProcessInfo
        {
            Pid = 101,
            ParentPid = 100,
            Name = "Child.exe",
            CpuPercentage = 1.25,
            MemoryBytes = (long)5.12e5,
            VirtualMemoryBytes = (long)6e5,
            ThreadCount = 1,
            HandleCount = 15,
            IsService = false,
            ProcessPath = null,
            Parameters = "|pipe|line",
            CreateTime = (long)1.32000000000000001e17
        };
        root.Children.Add(child);

        var orphan = new ProcessInfo
        {
            Pid = 102,
            ParentPid = 0,
            Name = "Orphan.exe",
            CpuPercentage = 0,
            MemoryBytes = (long)6.4e4,
            VirtualMemoryBytes = (long)8e4,
            ThreadCount = 1,
            HandleCount = 5,
            IsService = false,
            ProcessPath = @"C:\Orphan\Orphan.exe",
            Parameters = "run",
            CreateTime = (long)1.32000000000000002e17
        };

        return [root, orphan];
    }

    [Test]
    public void DeepCloneShouldProduceIndependentTree()
    {
        // Arrange
        var source = BuildSampleSnapshot();
        // Clear the Path on a cloned node's PARENT should not affect the source (children are cloned lists).

        // Act
        var clone = ProcessTreeSnapshot.DeepClone(source);

        // Assert: structure preserved
        Assert.That(clone.Count, Is.EqualTo(2));
        Assert.That(clone[0].Children.Count, Is.EqualTo(1));
        Assert.That(clone[0].Children[0].Pid, Is.EqualTo(101));

        // Assert: deep independence — mutating the clone must not touch the source.
        clone[0].Name = "Mutated.exe";
        clone[0].Children.Clear();
        Assert.That(source[0].Name, Is.EqualTo("Base.exe"));
        Assert.That(source[0].Children.Count, Is.EqualTo(1));
    }

    [Test]
    public void FlattenVisibleShouldCollectProcessInfoPreservingHierarchy()
    {
        // Arrange: a visible-tree view-model graph mirrored from the sample snapshot.
        // SyncProcessCollection builds PIVM children from ProcessInfo.Children, so replicate here.
        var snapshot = BuildSampleSnapshot();
        var rootVm = new ProcessItemViewModel(snapshot[0]);      // Base (has child Child.exe)
        var childVm = new ProcessItemViewModel(snapshot[0].Children[0]); // Child.exe
        rootVm.Children.Add(childVm);
        var orphanVm = new ProcessItemViewModel(snapshot[1]);    // Orphan, no children
        var visible = new System.Collections.ObjectModel.ObservableCollection<ProcessItemViewModel> { rootVm, orphanVm };

        // Act
        var flat = ProcessTreeProjection.FlattenVisible(visible);

        // Assert: every visible node (and its descendants) is flattened into ProcessInfo,
        // and the parent/child hierarchy survives on ProcessInfo.Children.
        Assert.That(flat.Count, Is.EqualTo(3));                  // Base + Child + Orphan
        Assert.That(flat[0].Pid, Is.EqualTo(100));
        Assert.That(flat[0].Children.Count, Is.EqualTo(1));
        Assert.That(flat[0].Children[0].Pid, Is.EqualTo(101));
        Assert.That(flat.Any(p => p.Pid == 102), Is.True);
    }

    [Test]
    public async Task ExportCsvShouldWriteAllRowsAndEscapeQuotes()
    {
        var service = new ProcessExportService();
        string csvPath = Path.Combine(Path.GetTempPath(), $"sp_export_{Guid.NewGuid():N}.csv");
        try
        {
            var result = await service.ExportAsync(BuildSampleSnapshot(), csvPath, ExportFormat.Csv);

            Assert.That(result.IsSuccess, Is.True);
            var content = await File.ReadAllTextAsync(csvPath);

            // Header + 3 process rows (root, child, orphan).
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines.Length, Is.EqualTo(4));
            Assert.That(lines[0], Does.Contain("PID"));
            // Child command line contains both an embedded comma and a literal quote -> must be quoted with doubled quotes.
            Assert.That(content, Does.Contain("\"-arg1 value,\"\"quoted\"\"\""));
            Assert.That(content, Does.Contain("Child.exe"));
            Assert.That(content, Does.Contain("Orphan.exe"));
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    [Test]
    public async Task ExportJsonShouldNestChildrenAndQuoteStrings()
    {
        var service = new ProcessExportService();
        string jsonPath = Path.Combine(Path.GetTempPath(), $"sp_export_{Guid.NewGuid():N}.json");
        try
        {
            var result = await service.ExportAsync(BuildSampleSnapshot(), jsonPath, ExportFormat.Json);

            Assert.That(result.IsSuccess, Is.True);
            var content = await File.ReadAllTextAsync(jsonPath);

            Assert.That(content, Does.Contain("\"processCount\":2"));
            Assert.That(content, Does.Contain("\"name\":\"Base.exe\""));
            Assert.That(content, Does.Contain("\"name\":\"Child.exe\""));
            // Nested children + comma/quote escaping of a command line.
            Assert.That(content, Does.Contain("\"children\":["));
            Assert.That(content, Does.Contain("\\\"quoted\\\""));
            // Ensure the JSON is actually well-formed.
            Assert.That(IsValidJson(content), Is.True);
        }
        finally
        {
            File.Delete(jsonPath);
        }
    }

    [Test]
    public async Task ExportMarkdownShouldEscapePipesAndWriteTable()
    {
        var service = new ProcessExportService();
        string mdPath = Path.Combine(Path.GetTempPath(), $"sp_export_{Guid.NewGuid():N}.md");
        try
        {
            var result = await service.ExportAsync(BuildSampleSnapshot(), mdPath, ExportFormat.Markdown);

            Assert.That(result.IsSuccess, Is.True);
            var content = await File.ReadAllTextAsync(mdPath);

            Assert.That(content, Does.StartWith("# System Processes Snapshot"));
            Assert.That(content, Does.Contain("| PID | Parent PID |"));
            // Command line with a pipe must be escaped so the table stays well-formed.
            Assert.That(content, Does.Contain("\\|pipe\\|line"));
        }
        finally
        {
            File.Delete(mdPath);
        }
    }

    [Test]
    public async Task ExportWithBlankPathShouldFailWithoutWriting()
    {
        var service = new ProcessExportService();

        var result = await service.ExportAsync(BuildSampleSnapshot(), "   ", ExportFormat.Csv);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task ExportToUnwritablePathShouldReturnFailure()
    {
        var service = new ProcessExportService();
        // A directory cannot be a file target -> writing should fail.
        string invalidPath = Path.GetTempPath();

        var result = await service.ExportAsync(BuildSampleSnapshot(), invalidPath, ExportFormat.Csv);

        Assert.That(result.IsSuccess, Is.False);
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}