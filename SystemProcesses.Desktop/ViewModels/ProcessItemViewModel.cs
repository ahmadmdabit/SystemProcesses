using System.Collections.ObjectModel;
using System.Windows.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.ViewModels;

public partial class ProcessItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public ProcessInfo ProcessInfo { get; }
    public ObservableCollection<ProcessItemViewModel> Children { get; } = new();

    public int Pid => ProcessInfo.Pid;
    public string Name => ProcessInfo.Name;
    public string Parameters => ProcessInfo.Parameters;
    public bool IsService => ProcessInfo.IsService;
    public ImageSource? Icon => ProcessInfo.Icon;

    // Properties that change frequently
    public double CpuPercentage => ProcessInfo.CpuPercentage;
    public long MemoryBytes => ProcessInfo.MemoryBytes;
    public long VirtualMemoryBytes => ProcessInfo.VirtualMemoryBytes;

    public ProcessItemViewModel(ProcessInfo processInfo)
    {
        ProcessInfo = processInfo;
    }

    public void Refresh()
    {
        // Manually notify for properties backed by the Model to avoid duplication
        // We could check equality here to save even more UI thread time
        OnPropertyChanged(nameof(CpuPercentage));
        OnPropertyChanged(nameof(MemoryBytes));
        OnPropertyChanged(nameof(VirtualMemoryBytes));
    }
}