using System.Windows;
using System.Windows.Controls;
using SystemProcesses.Desktop.ViewModels;

namespace SystemProcesses.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
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
}
