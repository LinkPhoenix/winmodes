using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinModes.App.Services;
using Wpf.Ui.Controls;

namespace WinModes.App;

/// <summary>A program picked in <see cref="ProgramPickerWindow"/>: the name Windows gives its process, where it runs from, and a name to show.</summary>
internal sealed record PickedProgram(string Name, string? Path, string? Label);

/// <summary>
/// Lists the programs that are open now so that the user can tick the ones that should start a mode. Programs with a window come first
/// (a game, a browser, an editor); the background ones are one tick away. Read-only: nothing is started or closed from here.
/// </summary>
public partial class ProgramPickerWindow : FluentWindow
{
    private List<Item> _all = [];

    public ProgramPickerWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    /// <summary>What the user ticked, once the window is closed with "Add selected"; empty otherwise.</summary>
    internal IReadOnlyList<PickedProgram> Picked { get; private set; } = [];

    private async Task LoadAsync()
    {
        var items = await Task.Run(ReadPrograms);
        _all = items;
        LoadingText.Visibility = Visibility.Collapsed;
        ShowItems();
        if (await IconCache.PreloadAsync(items.Select(item => item.Path)))
        {
            foreach (var item in items)
            {
                item.Icon = IconCache.Peek(item.Path);
            }
        }
    }

    private static List<Item> ReadPrograms()
    {
        var withWindow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        withWindow.Add(process.ProcessName);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // The process ended while it was looked at.
                }
            }
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return [.. ProcessActions.Sample()
            .Where(node => node.ExecutablePath is not null && node.Pid != Environment.ProcessId
                && !node.ExecutablePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            .GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Item(group.Key, group.First().ExecutablePath!, DescribeProgram(group.First().ExecutablePath!, group.Key), withWindow.Contains(group.Key)))
            .OrderByDescending(item => item.HasWindow)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The name the program gives itself ("Steam", "Discord"), when its file has one, else the process name.</summary>
    internal static string DescribeProgram(string path, string fallback)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            return string.IsNullOrEmpty(description) ? fallback : description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return fallback;
        }
    }

    private void ShowItems()
    {
        var text = SearchBox.Text.Trim();
        var shown = _all
            .Where(item => (item.HasWindow || ShowBackground.IsChecked == true)
                && (text.Length == 0 || item.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase) || item.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
        List.ItemsSource = shown;
        EmptyText.Visibility = _all.Count > 0 && shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelection();
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ShowItems();

    private void OnFilterToggled(object sender, RoutedEventArgs e) => ShowItems();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();

    private void UpdateSelection()
    {
        var count = List.SelectedItems.Count;
        AddButton.IsEnabled = count > 0;
        CountText.Text = count == 0 ? "" : Loc.N(count, "1 program selected", "{0} programs selected");
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        Picked = [.. List.SelectedItems.OfType<Item>().Select(item => new PickedProgram(item.Name, item.Path, item.Title == item.Name ? null : item.Title))];
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class Item(string name, string path, string title, bool hasWindow) : INotifyPropertyChanged
    {
        private ImageSource? _icon;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name => name;
        public string Path => path;
        public string Title => title;
        public bool HasWindow => hasWindow;
        public string Subtitle => title == name ? "" : $" ({name})";
        public string Detail => Privacy.Path(path);

        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                foreach (var property in (string[])[nameof(Icon), nameof(GlyphVisibility)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
                }
            }
        }

        public Visibility GlyphVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
