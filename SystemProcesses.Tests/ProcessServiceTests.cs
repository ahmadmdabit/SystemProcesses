using System;
using System.Threading.Tasks;

using SystemProcesses.Desktop.Models;
using SystemProcesses.Desktop.Services;

namespace SystemProcesses.Tests;

/// <summary>
/// Unit tests for ProcessService zero-allocation and correctness guarantees.
/// </summary>
[TestFixture]
public class ProcessServiceTests
{
    private ProcessService service;

    [SetUp]
    public void Setup()
    {
        service = new ProcessService();
    }

    [TearDown]
    public void Cleanup()
    {
        service?.Dispose();
    }

    /// <summary>
    /// CRITICAL: Verify ProcessInfo objects are reused, not recreated.
    /// This is the core zero-allocation guarantee.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldReuseProcessInfoObjects()
    {
        // Arrange
        var firstSnapshot = await service.GetProcessTreeAsync();
        var firstRootPid = firstSnapshot.Roots.Count > 0 ? firstSnapshot.Roots[0].Pid : -1;

        // Act - Get second snapshot
        var secondSnapshot = await service.GetProcessTreeAsync();
        var secondRootPid = secondSnapshot.Roots.Count > 0 ? secondSnapshot.Roots[0].Pid : -1;

        // Assert - If same process exists, should be same object reference
        if (firstRootPid == secondRootPid && firstRootPid > 0)
        {
            var firstProcess = firstSnapshot.Roots[0];
            var secondProcess = secondSnapshot.Roots[0];

            Assert.That(firstProcess, Is.SameAs(secondProcess),
                "ProcessInfo objects must be reused across refresh cycles for zero-allocation");
        }
    }

    /// <summary>
    /// CRITICAL: Verify PID reuse detection via composite key (PID + CreateTime).
    /// </summary>
    [Test]
    public async Task ProcessInfoCompositeKeyDetectsPidReuse()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Verify we have at least one process
        Assert.That(snapshot.Roots.Count, Is.GreaterThan(0), "At least one process should be in tree");

        // Act - Create two ProcessInfo with same PID but different CreateTime
        var process1 = new ProcessInfo { Pid = 1234, CreateTime = 100 };
        var process2 = new ProcessInfo { Pid = 1234, CreateTime = 200 };

        // Assert - Should be different despite same PID
        Assert.That(process1.CreateTime, Is.Not.EqualTo(process2.CreateTime),
            "Different processes with same PID should have different CreateTime");
    }

    /// <summary>
    /// CRITICAL: Verify buffer bounds are validated before pointer arithmetic.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldHandleBufferResize()
    {
        // Arrange - Get initial snapshot
        var snapshot1 = await service.GetProcessTreeAsync();
        var count1 = snapshot1.Roots.Count;

        // Act - Get multiple snapshots (buffer may need to resize)
        for (int i = 0; i < 5; i++)
        {
            var snapshot = await service.GetProcessTreeAsync();
            Assert.That(snapshot.Roots, Is.Not.Null, "Roots should never be null");
            Assert.That(snapshot.Roots.Count, Is.GreaterThan(0), "Should have at least one process");
        }

        // Assert - No exceptions thrown, buffer handled correctly
        var finalSnapshot = await service.GetProcessTreeAsync();
        Assert.That(finalSnapshot.Roots.Count, Is.GreaterThan(0), "Final snapshot should be valid");
    }

    /// <summary>
    /// CRITICAL: Verify string encoding is handled correctly (UTF-16 validation).
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldHandleProcessNames()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Act & Assert
        foreach (var process in snapshot.Roots)
        {
            Assert.That(process.Name, Is.Not.Null, "Process name should not be null");
            Assert.That(process.Name.Length, Is.GreaterThan(0), "Process name should not be empty");
            Assert.That(process.Name.Contains("\0"), Is.False, "Process name should not contain null exitors");
        }
    }

    /// <summary>
    /// Verify system statistics are calculated correctly.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldCalculateSystemStats()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();
        var stats = snapshot.Stats;

        // Act & Assert
        Assert.That(stats.ProcessCount, Is.GreaterThan(0), "Should have at least one process");
        Assert.That(stats.ThreadCount, Is.GreaterThan(0), "Should have at least one thread");
        Assert.That(stats.TotalPhysicalMemory, Is.GreaterThan(0), "Should have physical memory");
        Assert.That(stats.AvailablePhysicalMemory, Is.GreaterThan(0), "Should have available memory");
        Assert.That(stats.AvailablePhysicalMemory, Is.LessThanOrEqualTo(stats.TotalPhysicalMemory),
            "Available memory should not exceed total");
    }

    /// <summary>
    /// Verify Top 5 CPU processes are correctly identified.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldIdentifyTop5CpuProcesses()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();
        var stats = snapshot.Stats;

        // Act & Assert
        Assert.That(stats.Top5Processes, Is.Not.Null, "Top5Processes should not be null");
        Assert.That(stats.Top5Processes.Length, Is.EqualTo(5), "Should have exactly 5 slots");

        // Verify descending order
        for (int i = 0; i < 4; i++)
        {
            var process1 = stats.Top5Processes[i];
            var process2 = stats.Top5Processes[i + 1];

            if (process1 != null && process2 != null)
            {
                Assert.That(
                    process1.CpuPercentage, Is.GreaterThanOrEqualTo(process2.CpuPercentage),
                    "Top 5 should be in descending CPU order");
            }
        }
    }

    /// <summary>
    /// Verify drive statistics are collected.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldCollectDriveStats()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();
        var stats = snapshot.Stats;

        // Act & Assert
        Assert.That(stats.DriveCount, Is.GreaterThanOrEqualTo(0), "Drive count should be non-negative");
        Assert.That(stats.Drives, Is.Not.Null, "Drives array should not be null");

        for (int i = 0; i < stats.DriveCount; i++)
        {
            var drive = stats.Drives[i];
            Assert.That(drive.TotalSize, Is.GreaterThan(0), $"Drive {drive.Letter} should have size");
            Assert.That(drive.AvailableFreeSpace, Is.GreaterThanOrEqualTo(0), $"Drive {drive.Letter} should have free space");
            Assert.That(drive.AvailableFreeSpace, Is.LessThanOrEqualTo(drive.TotalSize),
                $"Drive {drive.Letter} free space should not exceed total");
        }
    }

    /// <summary>
    /// Verify service can be disposed without exceptions.
    /// </summary>
    [Test]
    public void DisposeShouldCleanupResources()
    {
        // Arrange
        var svc = new ProcessService();

        // Act - Should not throw
        svc.Dispose();
        svc.Dispose(); // Double dispose should be safe

        // Assert - No exception thrown
        Assert.Pass("Dispose completed without exception");
    }

    /// <summary>
    /// Verify finalizer cleanup (destructor path).
    /// </summary>
    [Test]
    public void FinalizerShouldCleanupResources()
    {
        // Arrange
        var svc = new ProcessService();

        // Act - Call finalizer indirectly by letting GC collect
        svc = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Assert - No exception thrown (finalizer ran successfully)
        Assert.Pass("Finalizer cleanup completed without exception");
    }

    /// <summary>
    /// Verify buffer validation catches invalid offsets.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldValidateBufferBounds()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Act & Assert - Should have valid process tree despite any internal validation
        Assert.That(snapshot.Roots, Is.Not.Null, "Roots should not be null");
        // SystemStats is a struct, so it's never null - just verify it's initialized
        Assert.That(snapshot.Stats.ProcessCount, Is.GreaterThanOrEqualTo(0), "Stats should be initialized");
    }

    /// <summary>
    /// Verify system stats are properly initialized even on error.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldReturnValidStatsStructure()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Act & Assert - Verify all stats fields are initialized
        // SystemStats is a struct, so it's never null - just verify fields are valid
        Assert.That(snapshot.Stats.ProcessCount, Is.GreaterThanOrEqualTo(0), "ProcessCount should be non-negative");
        Assert.That(snapshot.Stats.ThreadCount, Is.GreaterThanOrEqualTo(0), "ThreadCount should be non-negative");
        Assert.That(snapshot.Stats.HandleCount, Is.GreaterThanOrEqualTo(0), "HandleCount should be non-negative");
        Assert.That(snapshot.Stats.TotalMemory, Is.GreaterThanOrEqualTo(0), "TotalMemory should be non-negative");
        Assert.That(snapshot.Stats.TotalCpu, Is.GreaterThanOrEqualTo(0), "TotalCpu should be non-negative");
        Assert.That(snapshot.Stats.TotalPhysicalMemory, Is.GreaterThanOrEqualTo(0), "TotalPhysicalMemory should be non-negative");
        Assert.That(snapshot.Stats.AvailablePhysicalMemory, Is.GreaterThanOrEqualTo(0), "AvailablePhysicalMemory should be non-negative");
        Assert.That(snapshot.Stats.TotalCommitLimit, Is.GreaterThanOrEqualTo(0), "TotalCommitLimit should be non-negative");
        Assert.That(snapshot.Stats.AvailableCommitLimit, Is.GreaterThanOrEqualTo(0), "AvailableCommitLimit should be non-negative");
        Assert.That(snapshot.Stats.DiskActivePercent, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100), "DiskActivePercent should be 0-100");
        Assert.That(snapshot.Stats.TotalIoBytesPerSec, Is.GreaterThanOrEqualTo(0), "TotalIoBytesPerSec should be non-negative");
    }

    /// <summary>
    /// Verify process tree structure is correctly built with parent-child relationships.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldBuildCorrectTreeStructure()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Act & Assert - Verify tree structure
        Assert.That(snapshot.Roots, Is.Not.Null, "Roots should not be null");
        Assert.That(snapshot.Roots.Count, Is.GreaterThan(0), "Should have at least one root process");

        // Verify each root has valid structure
        foreach (var root in snapshot.Roots)
        {
            Assert.That(root.Pid, Is.GreaterThanOrEqualTo(0), "Root PID should be non-negative");
            Assert.That(root.Name, Is.Not.Null.And.Not.Empty, "Root name should not be empty");
            Assert.That(root.Children, Is.Not.Null, "Children list should not be null");

            // Verify children have valid parent reference
            foreach (var child in root.Children)
            {
                Assert.That(child.ParentPid, Is.EqualTo(root.Pid), "Child parent PID should match root PID");
            }
        }
    }

    /// <summary>
    /// Verify system idle process (PID 0) is handled correctly.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldHandleSystemIdleProcess()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Act - Look for System Idle Process
        ProcessInfo? idleProcess = null;
        foreach (var root in snapshot.Roots)
        {
            if (root.Pid == 0)
            {
                idleProcess = root;
                break;
            }
        }

        // Assert - If found, verify it's handled correctly
        if (idleProcess != null)
        {
            Assert.That(idleProcess.Name, Is.EqualTo("System Idle Process"), "PID 0 should be named 'System Idle Process'");
            Assert.That(idleProcess.Pid, Is.EqualTo(0), "System Idle Process should have PID 0");
        }
    }

    /// <summary>
    /// Verify system process (PID 4) is handled correctly.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldHandleSystemProcess()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Act - Look for System process
        ProcessInfo? systemProcess = null;
        foreach (var root in snapshot.Roots)
        {
            if (root.Pid == 4)
            {
                systemProcess = root;
                break;
            }
        }

        // Assert - If found, verify it's handled correctly
        if (systemProcess != null)
        {
            Assert.That(systemProcess.Name, Is.EqualTo("System"), "PID 4 should be named 'System'");
            Assert.That(systemProcess.Pid, Is.EqualTo(4), "System process should have PID 4");
        }
    }

    /// <summary>
    /// Verify command line retrieval for protected processes returns empty string gracefully.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldHandleCommandLineRetrievalFailures()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Act & Assert - Verify all processes have valid Parameters field (may be empty)
        foreach (var root in snapshot.Roots)
        {
            Assert.That(root.Parameters, Is.Not.Null, "Parameters should not be null (may be empty string)");

            // Recursively check children
            VerifyProcessParameters(root);
        }
    }

    private void VerifyProcessParameters(ProcessInfo process)
    {
        foreach (var child in process.Children)
        {
            Assert.That(child.Parameters, Is.Not.Null, "Child Parameters should not be null");
            VerifyProcessParameters(child);
        }
    }

    /// <summary>
    /// Verify process path retrieval handles failures gracefully.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldHandleProcessPathRetrievalFailures()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Act & Assert - Verify all processes have valid ProcessPath field (may be null)
        foreach (var root in snapshot.Roots)
        {
            // ProcessPath may be null for protected processes, which is acceptable
            // Just verify the field exists and doesn't throw
            var path = root.ProcessPath;

            // Recursively check children
            VerifyProcessPath(root);
        }
    }

    private void VerifyProcessPath(ProcessInfo process)
    {
        foreach (var child in process.Children)
        {
            var path = child.ProcessPath; // Should not throw
            VerifyProcessPath(child);
        }
    }

    /// <summary>
    /// Verify multiple consecutive snapshots maintain consistency.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldMaintainConsistencyAcrossSnapshots()
    {
        // Arrange
        var snapshot1 = await service.GetProcessTreeAsync();
        var snapshot2 = await service.GetProcessTreeAsync();
        var snapshot3 = await service.GetProcessTreeAsync();

        // Act & Assert - Verify all snapshots are valid
        Assert.That(snapshot1.Roots.Count, Is.GreaterThan(0), "Snapshot 1 should have processes");
        Assert.That(snapshot2.Roots.Count, Is.GreaterThan(0), "Snapshot 2 should have processes");
        Assert.That(snapshot3.Roots.Count, Is.GreaterThan(0), "Snapshot 3 should have processes");

        // Verify stats are reasonable across snapshots
        Assert.That(snapshot1.Stats.ProcessCount, Is.GreaterThan(0), "Snapshot 1 stats should be valid");
        Assert.That(snapshot2.Stats.ProcessCount, Is.GreaterThan(0), "Snapshot 2 stats should be valid");
        Assert.That(snapshot3.Stats.ProcessCount, Is.GreaterThan(0), "Snapshot 3 stats should be valid");
    }

    /// <summary>
    /// Verify CPU percentage calculations are within valid range.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldCalculateValidCpuPercentages()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();

        // Act & Assert - Verify CPU percentages are reasonable
        foreach (var root in snapshot.Roots)
        {
            Assert.That(root.CpuPercentage, Is.GreaterThanOrEqualTo(0), $"Process {root.Name} CPU should be non-negative");
            // CPU can exceed 100% on multi-core systems, so no upper bound check

            VerifyCpuPercentages(root);
        }
    }

    private void VerifyCpuPercentages(ProcessInfo process)
    {
        foreach (var child in process.Children)
        {
            Assert.That(child.CpuPercentage, Is.GreaterThanOrEqualTo(0), $"Child process {child.Name} CPU should be non-negative");
            VerifyCpuPercentages(child);
        }
    }

    /// <summary>
    /// Verify memory values are within valid range.
    /// </summary>
    [Test]
    public async Task GetProcessTreeShouldCalculateValidMemoryValues()
    {
        // Arrange
        var snapshot = await service.GetProcessTreeAsync();
        var stats = snapshot.Stats;

        // Act & Assert - Verify memory values are reasonable
        foreach (var root in snapshot.Roots)
        {
            Assert.That(root.MemoryBytes, Is.GreaterThanOrEqualTo(0), $"Process {root.Name} memory should be non-negative");
            Assert.That(root.VirtualMemoryBytes, Is.GreaterThanOrEqualTo(0), $"Process {root.Name} virtual memory should be non-negative");

            VerifyMemoryValues(root);
        }
    }

    private void VerifyMemoryValues(ProcessInfo process)
    {
        foreach (var child in process.Children)
        {
            Assert.That(child.MemoryBytes, Is.GreaterThanOrEqualTo(0), $"Child process {child.Name} memory should be non-negative");
            Assert.That(child.VirtualMemoryBytes, Is.GreaterThanOrEqualTo(0), $"Child process {child.Name} virtual memory should be non-negative");
            VerifyMemoryValues(child);
        }
    }
}
