using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.ViewModels
{
    public class ProcessItemViewModel : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isSelected;

        public ProcessInfo ProcessInfo { get; }
        public ObservableCollection<ProcessItemViewModel> Children { get; }

        public int Pid => ProcessInfo.Pid;
        public string Name => ProcessInfo.Name;
        public double CpuPercentage => ProcessInfo.CpuPercentage;
        public long MemoryBytes => ProcessInfo.MemoryBytes;
        public long VirtualMemoryBytes => ProcessInfo.VirtualMemoryBytes;
        public string Parameters => ProcessInfo.Parameters;
        public bool IsService => ProcessInfo.IsService;
        public ImageSource? Icon => ProcessInfo.Icon;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public ProcessItemViewModel(ProcessInfo processInfo)
        {
            ProcessInfo = processInfo;
            Children = new ObservableCollection<ProcessItemViewModel>(
                processInfo.Children.Select(c => new ProcessItemViewModel(c))
            );
        }

        public void UpdateFromProcessInfo(ProcessInfo newInfo)
        {
            ProcessInfo.CpuPercentage = newInfo.CpuPercentage;
            ProcessInfo.MemoryBytes = newInfo.MemoryBytes;
            ProcessInfo.VirtualMemoryBytes = newInfo.VirtualMemoryBytes;
            ProcessInfo.Parameters = newInfo.Parameters;

            OnPropertyChanged(nameof(CpuPercentage));
            OnPropertyChanged(nameof(MemoryBytes));
            OnPropertyChanged(nameof(VirtualMemoryBytes));
            OnPropertyChanged(nameof(Parameters));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
