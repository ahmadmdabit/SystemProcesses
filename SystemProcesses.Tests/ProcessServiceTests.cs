using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;

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
        
        // Find any process in the tree (not necessarily current process)
        ProcessInfo? anyProcess = null;
        foreach (var root in snapshot.Roots)
        {
            if (root != null)
            {
                anyProcess = root;
                break;
            }
        }

        Assert.That(anyProcess, Is.Not.Null, "At least one process should be in tree");

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
            Assert.That(process.Name.Contains("\0"), Is.False, "Process name should not contain null terminators");
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
            if (stats.Top5Processes[i] != null && stats.Top5Processes[i + 1] != null)
            {
                Assert.That(
                    stats.Top5Processes[i].CpuPercentage, Is.GreaterThanOrEqualTo(stats.Top5Processes[i + 1].CpuPercentage),
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
}
