using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using SystemProcesses.Desktop.Models;
using SystemProcesses.Desktop.Services;

namespace SystemProcesses.Desktop.ViewModels;

public partial class ProcessItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    private int _depth;

    public ProcessInfo ProcessInfo { get; }
    public ObservableCollection<ProcessItemViewModel> Children { get; } = new();

    public int Pid => ProcessInfo.Pid;
    public string PidText => ProcessInfo.PidText;
    public string Name => ProcessInfo.Name;
    public string Parameters => ProcessInfo.Parameters;
    public bool IsService => ProcessInfo.IsService;

    // Properties that change frequently
    public double CpuPercentage => ProcessInfo.CpuPercentage;
    public long MemoryBytes => ProcessInfo.MemoryBytes;
    public long VirtualMemoryBytes => ProcessInfo.VirtualMemoryBytes;

    public ProcessItemViewModel(ProcessInfo processInfo)
    {
        ProcessInfo = processInfo;
        if (!string.IsNullOrEmpty(ProcessInfo.ProcessPath))
        {
            _ = LoadIconAsync();
        }
    }

    private async Task LoadIconAsync()
    {
        // Offload GDI+ extraction to ThreadPool to avoid UI freeze
        var icon = await Task.Run(() => IconCache.GetIcon(ProcessInfo.ProcessPath));

        if (icon != null)
        {
            // Marshal back to UI thread (ObservableProperty handles PropertyChanged)
            Icon = icon;
        }
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