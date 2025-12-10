using System.Windows;
using System.Windows.Controls;
using SystemProcesses.Desktop.ViewModels;

namespace SystemProcesses.Desktop
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is MainViewModel viewModel && e.NewValue is ProcessItemViewModel selectedProcess)
            {
                viewModel.SelectedProcess = selectedProcess;
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
}
