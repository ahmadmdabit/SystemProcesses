using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.ViewModels;

public class ProcessItemViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public ProcessInfo ProcessInfo { get; }

    // Collection is now managed exclusively by MainViewModel.SyncProcessCollection
    public ObservableCollection<ProcessItemViewModel> Children { get; } = new();

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
    }

    public void Refresh()
    {
        // Notify UI of data changes
        // Note: We check values to avoid unnecessary events if data is stable
        OnPropertyChanged(nameof(CpuPercentage));
        OnPropertyChanged(nameof(MemoryBytes));
        OnPropertyChanged(nameof(VirtualMemoryBytes));

        // Note: Name, Pid, Icon, IsService usually don't change.
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}