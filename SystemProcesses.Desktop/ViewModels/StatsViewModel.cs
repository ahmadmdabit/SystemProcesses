using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SystemProcesses.Desktop.Helpers;
using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.ViewModels;

/// <summary>
/// ViewModel for the StatsView window displaying real-time system statistics.
/// </summary>
public partial class StatsViewModel : ObservableObject
{
    // PRIVATE FIELDS

    private readonly ILiteDialogService liteDialogService;

    // Flag to prevent infinite loops during shutdown
    public bool IsExitConfirmed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCpuUsage))]
    [NotifyPropertyChangedFor(nameof(RamPercent))]
    [NotifyPropertyChangedFor(nameof(VMPercent))]
    [NotifyPropertyChangedFor(nameof(DiskActivePercent))]
    private SystemStats currentStats;

    // OBSERVABLE COLLECTIONS

    /// <summary>
    /// Observable collection of drive statistics for dynamic UI binding.
    /// Reused across updates to avoid allocations.
    /// </summary>
    public ObservableCollection<DriveStatsViewModel> Drives { get; } = new();

    // COMPUTED PROPERTIES

    /// <summary>
    /// Total CPU usage percentage (0-100).
    /// </summary>
    public double TotalCpuUsage => CurrentStats.TotalCpu;

    /// <summary>
    /// Physical RAM usage percentage (0-100).
    /// </summary>
    public double RamPercent
    {
        get
        {
            double ramPercent = 0;
            if (CurrentStats.TotalPhysicalMemory > 0)
                ramPercent = ((double)(CurrentStats.TotalPhysicalMemory - CurrentStats.AvailablePhysicalMemory) / CurrentStats.TotalPhysicalMemory) * 100;
            return ramPercent;
        }
    }

    /// <summary>
    /// Virtual Memory (Commit Charge) usage percentage (0-100).
    /// </summary>
    public double VMPercent
    {
        get
        {
            double vmPercent = 0;
            if (CurrentStats.TotalCommitLimit > 0)
                vmPercent = ((double)(CurrentStats.TotalCommitLimit - CurrentStats.AvailableCommitLimit) / CurrentStats.TotalCommitLimit) * 100;
            return vmPercent;
        }
    }

    /// <summary>
    /// Disk active time percentage (0-100).
    /// </summary>
    public double DiskActivePercent => CurrentStats.DiskActivePercent;

    public StatsViewModel() : this(new LiteDialogService())
    {
    }

    public StatsViewModel(ILiteDialogService liteDialogService)
    {
        this.liteDialogService = liteDialogService;
    }

    // PUBLIC METHODS

    /// <summary>
    /// Updates system statistics and refreshes drive collection.
    /// Must be called from UI thread or will marshal to UI thread.
    /// </summary>
    /// <param name="stats">New system statistics</param>
    public void UpdateStats(SystemStats stats)
    {
        // Update stats struct (triggers all property notifications via NotifyPropertyChangedFor)
        CurrentStats = stats;

        // Update drives collection (differential update to minimize UI notifications)
        UpdateDrivesCollection(stats.Drives);
    }

    // PRIVATE METHODS

    /// <summary>
    /// Updates Drives observable collection with differential sync.
    /// Reuses existing ViewModels where possible to avoid allocations.
    /// </summary>
    private void UpdateDrivesCollection(DriveStats[]? newDrives)
    {
        if (newDrives == null || newDrives.Length == 0)
        {
            Drives.Clear();
            return;
        }

        // Remove drives no longer present
        for (int i = Drives.Count - 1; i >= 0; i--)
        {
            bool found = false;
            for (int j = 0; j < newDrives.Length; j++)
            {
                if (Drives[i].DriveLetter == newDrives[j].Letter)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Drives.RemoveAt(i);
            }
        }

        // Update existing or add new drives
        for (int i = 0; i < newDrives.Length; i++)
        {
            var newDrive = newDrives[i];

            if (newDrive.Letter == '\0')
            {
                continue;
            }

            DriveStatsViewModel? existing = null;

            // Find existing ViewModel for this drive
            for (int j = 0; j < Drives.Count; j++)
            {
                if (Drives[j].DriveLetter == newDrive.Letter)
                {
                    existing = Drives[j];
                    break;
                }
            }

            if (existing != null)
            {
                // Update existing (in-place, zero allocation)
                existing.Update(newDrive);
            }
            else
            {
                // Add new drive ViewModel
                Drives.Add(new DriveStatsViewModel(newDrive));
            }
        }
    }


    [RelayCommand]
    private void ShowApplication()
    {
        var window = Application.Current.MainWindow;
        if (window != null)
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Show();
            window.Activate();
        }
    }

    [RelayCommand]
    private async Task ExitApplication()
    {
        if (await ConfirmExitAsync())
        {
            Application.Current.Shutdown();
        }
    }

    // Shared Confirmation Logic
    public async Task<bool> ConfirmExitAsync()
    {
        if (IsExitConfirmed) return true;

        if (await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "Exit Application",
                message: "Are you sure you want to exit System Processes?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Question
            )) == LiteDialogResult.Yes)
        {
            IsExitConfirmed = true;
            return true;
        }

        return false;
    }
}

/// <summary>
/// ViewModel wrapper for DriveStats to enable property change notifications.
/// Lightweight wrapper that updates in-place to avoid allocations.
/// </summary>
public partial class DriveStatsViewModel : ObservableObject
{
    [ObservableProperty]
    private char driveLetter;

    [ObservableProperty]
    private long totalBytes;

    [ObservableProperty]
    private long freeBytes;

    [ObservableProperty]
    private double freePercent;

    /// <summary>
    /// Initializes a new instance with drive statistics.
    /// </summary>
    public DriveStatsViewModel(DriveStats stats)
    {
        DriveLetter = stats.Letter;
        TotalBytes = stats.TotalSize;
        FreeBytes = stats.AvailableFreeSpace;

        double percent = 0;
        if (stats.TotalSize > 0)
            percent = (double)stats.AvailableFreeSpace / stats.TotalSize * 100.0;

        FreePercent = percent;
    }

    /// <summary>
    /// Updates this ViewModel with new drive statistics (in-place update).
    /// </summary>
    public void Update(DriveStats stats)
    {
        // Only update changed properties to minimize notifications
        if (TotalBytes != stats.TotalSize)
            TotalBytes = stats.TotalSize;

        if (FreeBytes != stats.AvailableFreeSpace)
            FreeBytes = stats.AvailableFreeSpace;

        double percent = 0;
        if (stats.TotalSize > 0)
            percent = (double)stats.AvailableFreeSpace / stats.TotalSize * 100.0;

        if (FreePercent != percent)
            FreePercent = percent;
    }

    /// <summary>
    /// Drive label for display (e.g., "C:", "D:").
    /// </summary>
    public string DriveLabel => $"{DriveLetter}:";
}
