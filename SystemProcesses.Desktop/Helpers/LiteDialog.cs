using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

using SystemProcesses.Desktop.Services;

namespace SystemProcesses.Desktop.Helpers;

// 1. The Contract
public interface ILiteDialogService
{
    /// <summary>
    /// Shows a dialog safely from any thread.
    /// </summary>
    Task<LiteDialogResult> ShowAsync(LiteDialogRequest request);
}

// 2. The Data Structures (Zero-Allocation / Structs)
public enum LiteDialogResult
{ None, OK, Cancel, Yes, No }

public enum LiteDialogButton
{ OK, OKCancel, YesNo }

public enum LiteDialogImage
{ None, Success, Question, Information, Warning, Error }

public readonly struct LiteDialogRequest
{
    public string Title { get; }
    public string Message { get; }
    public LiteDialogButton Buttons { get; }
    public LiteDialogImage Image { get; }

    public LiteDialogRequest(string title, string message, LiteDialogButton buttons = LiteDialogButton.OK, LiteDialogImage image = LiteDialogImage.None)
    {
        Title = title;
        Message = message;
        Buttons = buttons;
        Image = image;
    }
}

// 3. The Engine (Pooled Window - Code-Only for Minimal Resources)
// Inherits from Window but avoids XAML parsing overhead.
internal sealed class LiteDialogWindow : Window
{
    // 1. Pre-calculated, Frozen Geometries (Zero-Allocation at Runtime)
    // Simple Material Design style paths
    private static readonly Geometry GeoSucess = Geometry.Parse("M2 12c0-4.714 0-7.071 1.464-8.536C4.93 2 7.286 2 12 2s7.071 0 8.535 1.464C22 4.93 22 7.286 22 12s0 7.071-1.465 8.535C19.072 22 16.714 22 12 22s-7.071 0-8.536-1.465C2 19.072 2 16.714 2 12m6.5.5L11 15l4.5-5.5");

    private static readonly Geometry GeoInfo = Geometry.Parse("M12 17v-6m1-3a1 1 0 1 0-2 0 1 1 0 1 0 2 0M2 12c0-4.714 0-7.071 1.464-8.536C4.93 2 7.286 2 12 2s7.071 0 8.535 1.464C22 4.93 22 7.286 22 12s0 7.071-1.465 8.535C19.072 22 16.714 22 12 22s-7.071 0-8.536-1.465C2 19.072 2 16.714 2 12");
    private static readonly Geometry GeoWarn = Geometry.Parse("M12 7v6m1 3a1 1 0 1 0-2 0 1 1 0 1 0 2 0M2 12c0-4.714 0-7.071 1.464-8.536C4.93 2 7.286 2 12 2s7.071 0 8.535 1.464C22 4.93 22 7.286 22 12s0 7.071-1.465 8.535C19.072 22 16.714 22 12 22s-7.071 0-8.536-1.465C2 19.072 2 16.714 2 12");
    private static readonly Geometry GeoError = Geometry.Parse("M2 12c0-4.714 0-7.071 1.464-8.536C4.93 2 7.286 2 12 2s7.071 0 8.535 1.464C22 4.93 22 7.286 22 12s0 7.071-1.465 8.535C19.072 22 16.714 22 12 22s-7.071 0-8.536-1.465C2 19.072 2 16.714 2 12m7-3 6 6m0-6-6 6");
    private static readonly Geometry GeoQuestion = Geometry.Parse("M2 12c0-4.714 0-7.071 1.464-8.536C4.93 2 7.286 2 12 2s7.071 0 8.535 1.464C22 4.93 22 7.286 22 12s0 7.071-1.465 8.535C19.072 22 16.714 22 12 22s-7.071 0-8.536-1.465C2 19.072 2 16.714 2 12m8.125-3.125a1.875 1.875 0 1 1 2.828 1.615c-.475.281-.953.708-.953 1.26V13m1 3a1 1 0 1 0-2 0 1 1 0 1 0 2 0");

    // Status icon brushes resolved from XAML resource dictionaries with fallback to hard-coded values.
    // Resolved lazily at runtime to avoid static constructor timing issues with Application.Current.
    private static SolidColorBrush ResolveSuccessBrush() => ResolveBrush("SuccessBrush", "#00AA00");
    private static SolidColorBrush ResolveQuestionBrush() => ResolveBrush("QuestionBrush", "#EF5D27");
    private static SolidColorBrush ResolveInfoBrush() => ResolveBrush("InfoBrush", "#2273DE");
    private static SolidColorBrush ResolveWarnBrush() => ResolveBrush("WarningBrush", "#E1AA15");
    private static SolidColorBrush ResolveErrorBrush() => ResolveBrush("ErrorBrush", "#AF002F");

    private static SolidColorBrush ResolveBrush(string resourceName, string fallbackHex)
    {
        if (Application.Current?.Resources[resourceName] is SolidColorBrush brush)
            return brush;
        return CreateFrozenBrush(fallbackHex);
    }

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    static LiteDialogWindow()
    {
        // Freeze for performance and thread safety
        if (GeoSucess.CanFreeze) GeoSucess.Freeze();
        if (GeoQuestion.CanFreeze) GeoQuestion.Freeze();
        if (GeoInfo.CanFreeze) GeoInfo.Freeze();
        if (GeoWarn.CanFreeze) GeoWarn.Freeze();
        if (GeoError.CanFreeze) GeoError.Freeze();
    }

    private readonly TextBlock txtTitle;
    private readonly TextBox txtMessage;
    private readonly Viewbox iconViewbox;
    private readonly Path iconPath;
    private readonly StackPanel pnlButtons;
    private readonly Button btn1;
    private readonly Button btn2;

    private LiteDialogResult result = LiteDialogResult.None;

    public LiteDialogWindow()
    {
        this.Style = Application.Current.Resources["WindowStyle"] as Style;

        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.ResizeMode = ResizeMode.NoResize;
        this.SizeToContent = SizeToContent.WidthAndHeight;
        this.ShowInTaskbar = false;
        this.Topmost = true;
        this.WindowStyle = WindowStyle.SingleBorderWindow;
        this.MinWidth = 350; // Slightly wider for icon
        this.MaxWidth = 600;

        var rootGrid = new Grid { Margin = new Thickness(15) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Content (Icon + Msg)
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        // 1. Title
        txtTitle = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 15),
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetRow(txtTitle, 0);
        rootGrid.Children.Add(txtTitle);

        // 2. Content Area (Grid for Icon + Message)
        var contentGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Col 0: Icon
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 1: Text

        // Icon Path
        iconPath = new Path
        {
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
        };

        // Icon Viewbox
        iconViewbox = new Viewbox
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 15, 0),
            Child = iconPath,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed
        };

        Grid.SetColumn(iconViewbox, 0);
        contentGrid.Children.Add(iconViewbox);

        // Message Text (read-only TextBox styled as TextBlock for selectability)
        txtMessage = new TextBox
        {
            Template = Application.Current.Resources["RoundedTextBoxTemplate"] as ControlTemplate,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Arrow,
            IsTabStop = false,
            Focusable = true
        };
        Grid.SetColumn(txtMessage, 1);
        contentGrid.Children.Add(txtMessage);

        Grid.SetRow(contentGrid, 1);
        rootGrid.Children.Add(contentGrid);

        // 3. Buttons
        pnlButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(pnlButtons, 2);
        rootGrid.Children.Add(pnlButtons);

        btn1 = CreateButton();
        btn2 = CreateButton();
        pnlButtons.Children.Add(btn1);
        pnlButtons.Children.Add(btn2);

        this.Content = rootGrid;
    }

    private Button CreateButton()
    {
        var b = new Button { MinWidth = 75, Margin = new Thickness(5, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        b.Click += (s, e) =>
        {
            if (s is Button btn && btn.Tag is LiteDialogResult res)
            {
                result = res;
                this.Hide();
            }
        };
        return b;
    }

    public LiteDialogResult Show(LiteDialogRequest request)
    {
        result = LiteDialogResult.None;
        txtTitle.Text = request.Title;
        txtMessage.Text = request.Message;
        this.Title = request.Title;

        // [Icon Logic]
        switch (request.Image)
        {
            case LiteDialogImage.Success: SetIcon(GeoSucess, ResolveSuccessBrush()); break;
            case LiteDialogImage.Question: SetIcon(GeoQuestion, ResolveQuestionBrush()); break;
            case LiteDialogImage.Information: SetIcon(GeoInfo, ResolveInfoBrush()); break;
            case LiteDialogImage.Warning: SetIcon(GeoWarn, ResolveWarnBrush()); break;
            case LiteDialogImage.Error: SetIcon(GeoError, ResolveErrorBrush()); break;
            default: iconViewbox.Visibility = Visibility.Collapsed; break;
        }

        // [Button Logic - Same as before]
        btn1.Visibility = Visibility.Collapsed;
        btn2.Visibility = Visibility.Collapsed;

        switch (request.Buttons)
        {
            case LiteDialogButton.OK:
                SetupButton(btn2, "OK", LiteDialogResult.OK, true);
                break;
            case LiteDialogButton.OKCancel:
                SetupButton(btn1, "OK", LiteDialogResult.OK, true);
                SetupButton(btn2, "Cancel", LiteDialogResult.Cancel, false);
                break;
            case LiteDialogButton.YesNo:
                SetupButton(btn1, "Yes", LiteDialogResult.Yes, true);
                SetupButton(btn2, "No", LiteDialogResult.No, false);
                break;
        }

        // .........................................................
        // Manual Centering for Pooled Window (with multi-monitor support)
        // .........................................................
        // WindowStartupLocation only works on first Show()
        // For reused windows, we must manually center

        // Force layout update to calculate new size based on content
        this.SizeToContent = SizeToContent.WidthAndHeight;
        this.UpdateLayout();

        // Center the dialog
        if (this.Owner != null && this.Owner.IsVisible)
        {
            // Center on owner window
            // Use RestoreBounds for maximized windows (Left/Top are negative when maximized)
            var ownerBounds = this.Owner.WindowState == WindowState.Maximized
                ? this.Owner.RestoreBounds
                : new Rect(this.Owner.Left, this.Owner.Top, this.Owner.ActualWidth, this.Owner.ActualHeight);

            this.Left = ownerBounds.Left + (ownerBounds.Width - this.ActualWidth) / 2;
            this.Top = ownerBounds.Top + (ownerBounds.Height - this.ActualHeight) / 2;

            // Ensure dialog stays within the screen where owner is located
            EnsureOnScreen(ownerBounds);
        }
        else
        {
            // Center on primary screen
            var workArea = SystemParameters.WorkArea;
            this.Left = workArea.Left + (workArea.Width - this.ActualWidth) / 2;
            this.Top = workArea.Top + (workArea.Height - this.ActualHeight) / 2;
        }

        this.ShowDialog();
        return result;
    }

    private void EnsureOnScreen(Rect ownerBounds)
    {
        // Find the monitor that contains the owner window center point
        // This ensures dialog appears on the same monitor as the owner
        var ownerCenter = new SystemPrimitives.Point
        {
            x = (int)(ownerBounds.Left + ownerBounds.Width / 2),
            y = (int)(ownerBounds.Top + ownerBounds.Height / 2)
        };

        // Get monitor handle for the point where owner window is located
        IntPtr hMonitor = SystemPrimitives.MonitorFromPoint(
            ownerCenter,
            SystemPrimitives.MonitorDefaultToNearest);

        if (hMonitor != IntPtr.Zero)
        {
            // Get monitor info including work area (excludes taskbar)
            var monitorInfo = new SystemPrimitives.MonitorInfo
            {
                cbSize = (uint)Marshal.SizeOf<SystemPrimitives.MonitorInfo>()
            };

            if (SystemPrimitives.GetMonitorInfoW(hMonitor, ref monitorInfo))
            {
                var workArea = monitorInfo.rcWork;

                // Adjust if off-screen horizontally
                if (this.Left < workArea.left)
                    this.Left = workArea.left;
                else if (this.Left + this.ActualWidth > workArea.right)
                    this.Left = workArea.right - this.ActualWidth;

                // Adjust if off-screen vertically
                if (this.Top < workArea.top)
                    this.Top = workArea.top;
                else if (this.Top + this.ActualHeight > workArea.bottom)
                    this.Top = workArea.bottom - this.ActualHeight;
            }
        }
    }

    private void SetIcon(Geometry data, Brush brush)
    {
        iconPath.Data = data;
        iconPath.Stroke = brush;
        iconPath.Fill = null;
        iconViewbox.Visibility = Visibility.Visible;
    }

    private void SetupButton(Button btn, string text, LiteDialogResult result, bool isDefault)
    {
        btn.Content = text;
        btn.Tag = result;
        btn.IsDefault = isDefault;
        btn.Visibility = Visibility.Visible;
        if (isDefault) btn.Focus();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        this.Hide();
    }
}

// 4. The Manager (Thread-Safe Singleton with Deadlock Prevention)
public class LiteDialogService : ILiteDialogService, IDisposable
{
    private LiteDialogWindow? pooledWindow;
    private readonly SemaphoreSlim locker = new(1, 1);
    private readonly Dispatcher uiDispatcher;

    public LiteDialogService()
    {
        // Capture the UI dispatcher immediately or fallback to current
        uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public async Task<LiteDialogResult> ShowAsync(LiteDialogRequest request)
    {
        await locker.WaitAsync();

        try
        {
            // CRITICAL: Check if already on UI thread to prevent deadlock
            // If UI thread calls ShowAsync() and we marshal to UI thread,
            // we deadlock (UI thread waits for work queued on itself)
            if (uiDispatcher.CheckAccess())
            {
                // Already on UI thread - execute directly
                return ShowInternal(request);
            }
            else
            {
                // On background thread - marshal to UI thread
                return await uiDispatcher.InvokeAsync(() => ShowInternal(request));
            }
        }
        finally
        {
            locker.Release();
        }
    }

    private LiteDialogResult ShowInternal(LiteDialogRequest request)
    {
        if (pooledWindow == null)
        {
            pooledWindow = new LiteDialogWindow();
        }

        // OPTIMIZED: O(1) Lookup via Win32
        Window? activeOwner = null;

        IntPtr hwnd = SystemPrimitives.GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            // Convert HWND back to WPF Window
            // This is extremely fast (hashtable lookup internally)
            if (System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.RootVisual is Window w)
            {
                // Ensure we don't set the dialog as its own owner
                if (w != pooledWindow && w.IsVisible)
                {
                    activeOwner = w;
                }
            }
        }

        // Fallback to MainWindow if OS reports nothing (or we found the dialog itself)
        activeOwner ??= Application.Current?.MainWindow;

        // Apply Owner & Location
        if (activeOwner != null && activeOwner.IsVisible)
        {
            pooledWindow.Owner = activeOwner;
            pooledWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            pooledWindow.Owner = null;
            pooledWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return pooledWindow.Show(request);
    }

    public void Dispose()
    {
        pooledWindow?.Close();
        pooledWindow = null;
        locker.Dispose();
    }
}