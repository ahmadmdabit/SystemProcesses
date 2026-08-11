using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.Win32;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Helpers;

/// <summary>
/// Contract for showing the LiteDialog-style export form (file path + format radio buttons).
/// </summary>
public interface IExportDialogService
{
    /// <summary>
    /// Shows the export dialog from any thread. Returns the user's choices, or <see langword="null"/>
    /// if the dialog was cancelled.
    /// </summary>
    Task<ProcessExportSettings?> ShowAsync();
}

/// <summary>
/// A lightweight, code-only export dialog (no XAML) that visually and structurally mirrors
/// <see cref="LiteDialogWindow"/>. Lets the user pick a destination file path (with a standard
/// browse dialog) and a format via three radio buttons: CSV / JSON / Markdown.
/// </summary>
internal sealed class ExportDialogWindow : Window
{
    private readonly TextBlock txtHeader;
    private readonly RadioButton rbCsv;
    private readonly RadioButton rbJson;
    private readonly RadioButton rbMarkdown;
    private readonly RadioButton rbFull;
    private readonly RadioButton rbVisible;
    private readonly TextBox txtPath;

    private bool accepted;

    public ExportDialogWindow()
    {
        this.Style = Application.Current.Resources["WindowStyle"] as Style;

        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.ResizeMode = ResizeMode.NoResize;
        this.SizeToContent = SizeToContent.WidthAndHeight;
        this.ShowInTaskbar = false;
        this.Topmost = true;
        this.Title = "Export Process Snapshot";
        this.MinWidth = 520;
        this.Width = 520;
        this.MaxWidth = 520;

        var root = new Grid { Margin = new Thickness(15) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // File path row
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Format group
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Export mode group
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        // Title
        var title = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 15),
            Text = "Export the current snapshot of running processes"
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        // Path row
        var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        txtPath = new TextBox
        {
            Template = Application.Current.Resources["RoundedTextBoxTemplate"] as ControlTemplate,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Destination file path"
        };
        Grid.SetColumn(txtPath, 0);
        pathRow.Children.Add(txtPath);

        var browse = new Button
        {
            Content = "Browse...",
            Margin = new Thickness(5, 0, 0, 0),
            MinWidth = 80
        };
        browse.Click += BrowseClick;
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(browse);
        Grid.SetRow(pathRow, 1);
        root.Children.Add(pathRow);

        var formatPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6)
        };

        txtHeader = new TextBlock
        {
            Text = "Export Type:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.Resources["SecondaryBrush"] as SolidColorBrush,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 6, 0),
        };
        rbCsv = CreateRadio("CSV", "Comma-separated values (.csv)", true);
        rbJson = CreateRadio("JSON", "Nested JavaScript Object Notation (.json)", false);
        rbMarkdown = CreateRadio("Markdown", "GitHub-flavored Markdown table (.md)", false);

        // When a format radio is checked, rewrite only the file extension (base name + directory kept).
        rbCsv.Checked += (_, _) => SyncExtensionToFormat(ExportFormat.Csv);
        rbJson.Checked += (_, _) => SyncExtensionToFormat(ExportFormat.Json);
        rbMarkdown.Checked += (_, _) => SyncExtensionToFormat(ExportFormat.Markdown);

        formatPanel.Children.Add(txtHeader);
        formatPanel.Children.Add(rbCsv);
        formatPanel.Children.Add(rbJson);
        formatPanel.Children.Add(rbMarkdown);
        Grid.SetRow(formatPanel, 2);
        root.Children.Add(formatPanel);

        // Export Mode panel (Full = latest snapshot, Visible = filtered view)
        var modePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6)
        };

        var modeHeader = new TextBlock
        {
            Text = "Export Mode:",
            Foreground = Application.Current.Resources["SecondaryBrush"] as SolidColorBrush,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        rbFull = CreateRadio("Full", "Export the full, latest process snapshot", true, "ExportMode");
        rbVisible = CreateRadio("Visible", "Export only the processes currently shown (search/isolation applied)", false, "ExportMode");

        modePanel.Children.Add(modeHeader);
        modePanel.Children.Add(rbFull);
        modePanel.Children.Add(rbVisible);
        Grid.SetRow(modePanel, 3);
        root.Children.Add(modePanel);

        // Buttons
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnOk = CreateActionButton("OK", isDefault: true);
        btnOk.Click += (_, _) => { accepted = true; this.Hide(); };

        var btnCancel = CreateActionButton("Cancel", isDefault: false);
        btnCancel.Click += (_, _) => { accepted = false; this.Hide(); };

        // Cancel = keyboard Escape, default path automatically set when the window is shown.
        this.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                accepted = false;
                this.Hide();
            }
        };

        buttons.Children.Add(btnOk);
        buttons.Children.Add(btnCancel);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        this.Content = root;
    }

    private RadioButton CreateRadio(string label, string tooltip, bool isChecked, string? groupName = null)
    {
        var rb = new RadioButton
        {
            Content = label,
            IsChecked = isChecked,
            GroupName = groupName ?? "ExportFormat",
            Margin = new Thickness(0, 2, 0, 2),
            ToolTip = tooltip
        };
        return rb;
    }

    private static Button CreateActionButton(string content, bool isDefault)
    {
        return new Button
        {
            Content = content,
            MinWidth = 75,
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2),
            IsDefault = isDefault
        };
    }

    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Choose the exported file path",
            FileName = "processesSnapshot",
            Filter = "CSV file (*.csv)|*.csv|JSON file (*.json)|*.json|Markdown file (*.md)|*.md"
        };

        if (sfd.ShowDialog(this) == true)
        {
            txtPath.Text = sfd.FileName;
            ApplyDefaultFileNameExtension(sfd.FileName);
        }
    }

    // Keep path and radio selection consistent with the chosen filter extension.
    private void ApplyDefaultFileNameExtension(string selectedPath)
    {
        if (selectedPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) rbCsv.IsChecked = true;
        else if (selectedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) rbJson.IsChecked = true;
        else if (selectedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) rbMarkdown.IsChecked = true;
    }

    // Rewrite only the file extension to match the selected format; the base name and directory are preserved.
    private void SyncExtensionToFormat(ExportFormat format)
    {
        var extension = format switch
        {
            ExportFormat.Json => ".json",
            ExportFormat.Markdown => ".md",
            _ => ".csv"
        };

        var path = txtPath.Text.Trim();
        if (path.Length == 0)
        {
            txtPath.Text = BuildDefaultPath(format);
            return;
        }

        var newPath = System.IO.Path.HasExtension(path)
            ? System.IO.Path.ChangeExtension(path, extension)
            : path + extension;
        if (newPath != txtPath.Text)
        {
            txtPath.Text = newPath;
        }
    }

    private ExportFormat SelectedFormat
    {
        get
        {
            if (rbJson.IsChecked == true) return ExportFormat.Json;
            if (rbMarkdown.IsChecked == true) return ExportFormat.Markdown;
            return ExportFormat.Csv;
        }
    }

    private ExportMode SelectedMode
    {
        get
        {
            if (rbVisible.IsChecked == true) return ExportMode.Visible;
            return ExportMode.Full;
        }
    }

    public ProcessExportSettings? ShowExport(ProcessExportSettings? initial = null)
    {
        accepted = false;
        txtPath.Text = initial?.FilePath ?? string.Empty;
        rbCsv.IsChecked = initial == null || initial.Value.Format == ExportFormat.Csv;
        rbJson.IsChecked = initial?.Format == ExportFormat.Json;
        rbMarkdown.IsChecked = initial?.Format == ExportFormat.Markdown;
        rbFull.IsChecked = initial == null || initial.Value.Mode == ExportMode.Full;
        rbVisible.IsChecked = initial?.Mode == ExportMode.Visible;

        // Auto-suggest a default file name based on the format radio default (CSV) when no path is set.
        if (string.IsNullOrWhiteSpace(txtPath.Text))
        {
            txtPath.Text = BuildDefaultPath(SelectedFormat);
        }

        // Center on owner (mirrors LiteDialogWindow) then show.
        CenterOnOwner();

        this.ShowDialog();
        if (!accepted)
        {
            return null;
        }

        var format = SelectedFormat;
        var mode = SelectedMode;
        if (string.IsNullOrWhiteSpace(txtPath.Text))
        {
            return null;
        }

        return new ProcessExportSettings(txtPath.Text.Trim(), format, mode);
    }

    private string BuildDefaultPath(ExportFormat format)
    {
        string extension = format switch
        {
            ExportFormat.Json => ".json",
            ExportFormat.Markdown => ".md",
            _ => ".csv"
        };

        string download = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents");
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return System.IO.Path.Combine(download, $"processesSnapshot-{stamp}{extension}");
    }

    private void CenterOnOwner()
    {
        this.SizeToContent = SizeToContent.WidthAndHeight;
        this.UpdateLayout();

        if (this.Owner != null && this.Owner.IsVisible)
        {
            var ownerBounds = this.Owner.WindowState == WindowState.Maximized
                ? this.Owner.RestoreBounds
                : new Rect(this.Owner.Left, this.Owner.Top, this.Owner.ActualWidth, this.Owner.ActualHeight);
            this.Left = ownerBounds.Left + (ownerBounds.Width - this.ActualWidth) / 2;
            this.Top = ownerBounds.Top + (ownerBounds.Height - this.ActualHeight) / 2;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            this.Left = workArea.Left + (workArea.Width - this.ActualWidth) / 2;
            this.Top = workArea.Top + (workArea.Height - this.ActualHeight) / 2;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        this.Hide();
    }
}

/// <summary>
/// Thread-safe host for <see cref="ExportDialogWindow"/>. Marshals to the UI dispatcher exactly like
/// <see cref="LiteDialogService"/> so the dialog can be shown from any thread without deadlocking.
/// </summary>
public class ExportDialogService : IExportDialogService
{
    private ExportDialogWindow? pooledWindow;
    private readonly SemaphoreSlim locker = new(1, 1);
    private readonly Dispatcher uiDispatcher;

    public ExportDialogService()
    {
        uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public async Task<ProcessExportSettings?> ShowAsync()
    {
        await locker.WaitAsync();
        try
        {
            if (uiDispatcher.CheckAccess())
            {
                return ShowInternal(null);
            }
            return await uiDispatcher.InvokeAsync(() => ShowInternal(null));
        }
        finally
        {
            locker.Release();
        }
    }

    private ProcessExportSettings? ShowInternal(ProcessExportSettings? initial)
    {
        pooledWindow ??= new ExportDialogWindow();

        // Mirror LiteDialog's owner resolution so the dialog appears on the active window/monitor.
        var activeOwner = Application.Current?.MainWindow;
        if (activeOwner != null && activeOwner.IsVisible)
        {
            pooledWindow.Owner = activeOwner;
        }
        else
        {
            pooledWindow.Owner = null;
        }

        return pooledWindow.ShowExport(initial);
    }
}