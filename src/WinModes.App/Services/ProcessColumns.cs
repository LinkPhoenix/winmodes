using System.ComponentModel;
using System.Windows;

namespace WinModes.App.Services;

/// <summary>
/// The optional columns of the Processes page, shared by its header and by every row: the header and the rows bind to this one object, so
/// choosing a column changes the whole list at once without rebuilding a single row. A hidden column has no width and its cells are collapsed.
/// </summary>
internal sealed class ProcessColumns : INotifyPropertyChanged
{
    private const double WorkingSetWidth = 90;
    private const double HandlesWidth = 70;
    private const double RunningForWidth = 90;
    private const double PriorityWidth = 90;

    public static ProcessColumns Shared { get; } = new();

    private ProcessColumnSettings _settings = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProcessColumnSettings Settings => _settings;

    public void Apply(ProcessColumnSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public GridLength WorkingSetColumn => Width(_settings.WorkingSet, WorkingSetWidth);

    public GridLength HandlesColumn => Width(_settings.Handles, HandlesWidth);

    public GridLength RunningForColumn => Width(_settings.RunningFor, RunningForWidth);

    public GridLength PriorityColumn => Width(_settings.Priority, PriorityWidth);

    public Visibility WorkingSetVisibility => Shown(_settings.WorkingSet);

    public Visibility HandlesVisibility => Shown(_settings.Handles);

    public Visibility RunningForVisibility => Shown(_settings.RunningFor);

    public Visibility PriorityVisibility => Shown(_settings.Priority);

    private static GridLength Width(bool shown, double width) => new(shown ? width : 0);

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
}
