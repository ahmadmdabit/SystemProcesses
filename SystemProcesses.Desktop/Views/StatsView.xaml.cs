using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

using Serilog;

using SystemProcesses.Desktop.Services;
using SystemProcesses.Desktop.ViewModels;

namespace SystemProcesses.Desktop.Views;

/// <summary>
/// StatsView window displaying real-time system statistics.
/// Updates are received from MainViewModel and marshalled to UI thread.
/// Docked to bottom of screen using Windows AppBar API.
/// </summary>
public partial class StatsView : Window
{
    private const int CornerRadius = 10;

    private readonly MainViewModel mainViewModel;
    private readonly StatsViewModel? statsViewModel;
    private IntPtr windowHandle;
    private HwndSource? hwndSource;
    private readonly ConcurrentDictionary<int, int> messageFrequency = new();
    private readonly DispatcherTimer updateTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    /// <summary>
    /// Initializes StatsView with reference to MainViewModel for data sharing.
    /// </summary>
    /// <param name="mainViewModel">Main view model containing system statistics</param>
    public StatsView(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));

        InitializeComponent();

        // Get StatsViewModel from DataContext
        statsViewModel = DataContext as StatsViewModel;

        // Wire up event handlers
        SourceInitialized += OnSourceInitialized;
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;

        // Subscribe to MainViewModel's stats updates
        mainViewModel.StatsUpdated += OnMainViewModelStatsUpdated;

        // Start periodic update timer
        updateTimer.Tick += (s, e) => EnsureTopmost();
        updateTimer.Start();
    }


    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Get window handle
        windowHandle = new WindowInteropHelper(this).Handle;

        // Position window at absolute bottom of screen (over taskbar)
        PositionAtBottom();

        // Initial stats sync
        if (statsViewModel != null)
        {
            SyncStatsFromMainViewModel();
        }
    }

    /// <summary>
    /// Event handler for MainViewModel stats updates.
    /// Marshals update to UI thread for thread-safe binding updates.
    /// </summary>
    private void OnMainViewModelStatsUpdated(object? sender, EventArgs e)
    {
        // Check if we're already on the UI thread
        if (Dispatcher.CheckAccess())
        {
            SyncStatsFromMainViewModel();
        }
        else
        {
            // Marshal to UI thread (non-blocking)
            Dispatcher.InvokeAsync(SyncStatsFromMainViewModel, DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// Synchronizes stats from MainViewModel to StatsViewModel.
    /// Must be called on UI thread.
    /// </summary>
    private void SyncStatsFromMainViewModel()
    {
        statsViewModel?.UpdateStats(mainViewModel.SystemStats);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        UpdateWindowRegion();

        // Hook into Windows message pump to intercept z-order changes
        hwndSource = HwndSource.FromHwnd(windowHandle);
        if (hwndSource != null)
        {
            hwndSource.AddHook(WndProc);
            Log.Information("WndProc hook registered for StatsView");
        }
        else
        {
            Log.Warning("Failed to get HwndSource for WndProc hook");
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateWindowRegion();
    }

    /// <summary>
    /// Enables dragging the borderless window.
    /// </summary>
    private void BorderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove can throw if mouse is released during the operation
                // Safe to ignore
            }
        }
    }

    /// <summary>
    /// Handles keyboard shortcuts (ESC to close).
    /// </summary>
    private void WindowKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    /// <summary>
    /// Cleanup: Stop timer, unhook WndProc, and unsubscribe from events to prevent memory leaks.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        // Stop update timer
        updateTimer.Stop();

        // Unhook from Windows message pump
        if (hwndSource != null)
        {
            hwndSource.RemoveHook(WndProc);
            hwndSource = null;
            Log.Information("WndProc hook removed");
        }

        // Unsubscribe from events to prevent memory leaks
        if (mainViewModel != null)
        {
            mainViewModel.StatsUpdated -= OnMainViewModelStatsUpdated;
        }

        // ADD THIS LOGGING:
        Log.Information("Message frequency summary:");
        foreach (var kvp in messageFrequency.OrderByDescending(x => x.Value))
        {
            string msgName = kvp.Key switch
            {
                SystemPrimitives.WmWindowPosChanging => "WmWindowPosChanging",
                SystemPrimitives.WmActivateApp => "WmActivateApp",
                SystemPrimitives.WmDisplayChange => "WmDisplayChange",
                SystemPrimitives.WmSettingChange => "WmSettingChange",
                SystemPrimitives.WmDwmComPositionChanged => "WmDwmComPositionChanged",
                _ => $"0x{kvp.Key:X4}"
            };
            Log.Information("  {MessageName}: {Count} times", msgName, kvp.Value);
        }

        base.OnClosed(e);
    }

    #region Windows API

    /// <summary>
    /// Positions window at the absolute bottom of the primary screen, overlaying the taskbar.
    /// Uses full screen bounds including taskbar area.
    /// </summary>
    private void PositionAtBottom()
    {
        try
        {
            // Get full screen dimensions (including taskbar area)
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            int windowHeight = (int)ActualHeight;

            // Ensure window has valid height
            if (windowHeight <= 0)
            {
                windowHeight = 46; // Default height from XAML
            }

            // Position at absolute bottom of screen
            Left = 0;
            Top = screenHeight - windowHeight;
            Width = screenWidth;
            Height = windowHeight;

            // Force window to topmost z-order (above taskbar)
            if (windowHandle != IntPtr.Zero && SystemPrimitives.IsWindow(windowHandle))
            {
                bool result = SystemPrimitives.SetWindowPos(
                    windowHandle,
                    SystemPrimitives.HwndTopMost,
                    0, 0, 0, 0,
                    SystemPrimitives.SpwNoMove | SystemPrimitives.SwpNoSize | SystemPrimitives.SwpNoActivate | SystemPrimitives.SwpShowWindow
                );

                if (!result)
                {
                    Log.Warning("SetWindowPos failed to set HwndTopMost - window may not appear above taskbar");
                }
                else
                {
                    Log.Information("StatsView set to HwndTopMost - should appear above taskbar");
                }
            }

            Log.Information("StatsView positioned at bottom - Left={Left}, Top={Top}, Width={Width}, Height={Height}, ScreenHeight={ScreenHeight}",
                Left, Top, Width, Height, screenHeight);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to position window at bottom");
        }
    }

    /// <summary>
    /// Windows message procedure hook. Intercepts window messages before WPF processes them.
    /// Prevents taskbar from pushing this window below in z-order.
    /// Zero-allocation: uses IntPtr directly without boxing.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // ADD THIS TRACKING CODE:
        if (msg == SystemPrimitives.WmWindowPosChanging ||
            msg == SystemPrimitives.WmActivateApp ||
            msg == SystemPrimitives.WmDisplayChange ||
            msg == SystemPrimitives.WmSettingChange ||
            msg == SystemPrimitives.WmDwmComPositionChanged)
        {
            messageFrequency.TryGetValue(msg, out int count);
            messageFrequency[msg] = count + 1;
        }

        switch (msg)
        {
            case SystemPrimitives.WmWindowPosChanging:
                // Intercept z-order changes and force HwndTopMost
                if (lParam != IntPtr.Zero)
                {
                    // Marshal structure from unmanaged memory
                    var windowPos = Marshal.PtrToStructure<SystemPrimitives.WindowPos>(lParam);

                    // Check if z-order is being changed
                    if ((windowPos.flags & SystemPrimitives.SwpNoZOrder) == 0)
                    {
                        // Force HwndTopMost to maintain position above taskbar
                        windowPos.hwndInsertAfter = SystemPrimitives.HwndTopMost;

                        // Write modified structure back to unmanaged memory
                        Marshal.StructureToPtr(windowPos, lParam, false);

                        Log.Debug("WmWindowPosChanging intercepted - enforcing HwndTopMost");
                    }
                }
                break;

            case SystemPrimitives.WmActivateApp:
                // Application-level activation (switching between apps)
                Log.Debug("WmActivateApp received - enforcing topmost");
                EnsureTopmost();
                break;

            case SystemPrimitives.WmDisplayChange:
                // Display resolution or orientation changed
                Log.Information("WmDisplayChange received - repositioning and enforcing topmost");
                PositionAtBottom(); // May need to reposition for new screen dimensions
                EnsureTopmost();
                break;

            case SystemPrimitives.WmSettingChange:
                // System settings changed (theme, taskbar position, etc.)
                if (lParam != IntPtr.Zero)
                {
                    string? setting = Marshal.PtrToStringUni(lParam);
                    if (setting == "WindowMetrics" || setting == "WorkArea")
                    {
                        Log.Information("WmSettingChange (WorkArea/WindowMetrics) - repositioning and enforcing topmost");
                        PositionAtBottom();
                    }
                }
                EnsureTopmost();
                break;

            case SystemPrimitives.WmDwmComPositionChanged:
                // DWM composition state changed (Aero on/off)
                Log.Information("WmDwmComPositionChanged received - enforcing topmost");
                EnsureTopmost();
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Ensures window remains in topmost z-order position.
    /// Zero-allocation: uses existing window handle and flags.
    /// Non-blocking: quick Win32 call on UI thread.
    /// </summary>
    private void EnsureTopmost()
    {
        // Validate window handle before attempting Win32 call
        // Prevents failures during window teardown/destruction
        if (windowHandle == IntPtr.Zero || !SystemPrimitives.IsWindow(windowHandle))
            return;

        // Use NOACTIVATE to avoid stealing focus from other applications
        // Include SHOWWINDOW to restore visibility after display/DWM changes
        bool result = SystemPrimitives.SetWindowPos(
            windowHandle,
            SystemPrimitives.HwndTopMost,
            0, 0, 0, 0,
            SystemPrimitives.SpwNoMove | SystemPrimitives.SwpNoSize | SystemPrimitives.SwpNoActivate | SystemPrimitives.SwpShowWindow
        );

        // Note: SetWindowPos can legitimately fail during window teardown
        // Even though IsWindow() returns true, the window may be in a state
        // where it cannot accept positioning changes. This is benign.
        // We don't log this as it creates noise during normal window close.
    }

    /// <summary>
    /// Updates window region to create rounded corners.
    /// Uses Win32 API for transparency compatibility.
    /// </summary>
    private void UpdateWindowRegion()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int w = Math.Max(0, (int)ActualWidth);
        int h = Math.Max(0, (int)ActualHeight);

        // Create rounded region
        IntPtr hrgn = SystemPrimitives.CreateRoundRectRgn(0, 0, w + 1, h + 1, CornerRadius, CornerRadius);

        if (hrgn == IntPtr.Zero)
        {
            Log.Debug("CreateRoundRectRgn failed");
            return;
        }

        // Set the window region
        int result = SystemPrimitives.SetWindowRgn(hwnd, hrgn, 1);

        if (result == 0)
        {
            // On failure, retrieve extended error
            int error = Marshal.GetLastWin32Error();
            Log.Debug($"SetWindowRgn failed with error {error}");

            // Clean up region handle on failure
            SystemPrimitives.DeleteObject(hrgn);
        }

        // Note: After successful SetWindowRgn, the OS owns the region handle.
        // Do NOT call DeleteObject on hrgn after success.
    }

    #endregion Windows API
}