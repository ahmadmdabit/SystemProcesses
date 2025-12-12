using System.Windows;
using System.Windows.Controls;

using SystemProcesses.Desktop.Helpers;
using SystemProcesses.Desktop.ViewModels;

namespace SystemProcesses.Desktop;

public partial class MainWindow : Window
{
    public double TreeViewRowWidth { get => (double)GetValue(TreeViewRowWidthProperty); set => SetValue(TreeViewRowWidthProperty, value); }

    public static readonly DependencyProperty TreeViewRowWidthProperty =
        DependencyProperty.Register(nameof(TreeViewRowWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(0.0));

    public readonly ILiteDialogService liteDialogService;

    public MainWindow()
    {
        liteDialogService = new LiteDialogService();

        InitializeComponent();
    }

    private void TreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel viewModel && e.NewValue is ProcessItemViewModel selectedProcess)
        {
            viewModel.SelectedProcess = selectedProcess;
        }
    }

    private void TreeViewItemPreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Force selection on right-click so the Context Menu commands apply to the clicked item
        if (sender is TreeViewItem treeViewItem)
        {
            treeViewItem.IsSelected = true;
            treeViewItem.Focus();
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
        base.OnClosed(e);
    }

    private void uiProcessTreeViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TreeViewRowWidth = uiProcessTreeView.ActualWidth - 30; // Adjust for padding and scrollbar
    }

    private void uiRootStateChanged(object sender, System.EventArgs e)
    {
        ShowInTaskbar = WindowState != WindowState.Minimized;
    }

    private void uiRootClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // 1. If already confirmed (by Tray or previous check), let it close.
            if (vm.IsExitConfirmed) return;

            // 2. Otherwise, CANCEL the immediate close.
            e.Cancel = true;

            // 3. Execute the exit application command.
            // Note: Because we cancelled, the window stays open while this runs.
            vm.ExitApplicationCommand.ExecuteAsync(null);
        }
    }
}