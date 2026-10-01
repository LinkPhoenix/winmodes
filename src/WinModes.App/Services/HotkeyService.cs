using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinModes.App.Services;

/// <summary>System-wide Ctrl+Alt shortcuts, received on a hidden message-only window.</summary>
internal sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    // Holding the keys down must not fire the action repeatedly.
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("WinModesHotkeys") { ParentWindow = MessageOnlyParent });
        _source.AddHook(OnMessage);
    }

    /// <summary>Registers Ctrl+Alt+key. Returns false when another app already owns the shortcut.</summary>
    public bool Register(uint virtualKey, Action action)
    {
        var id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, ModControl | ModAlt | ModNoRepeat, virtualKey))
        {
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _actions.Clear();
    }

    public void Dispose()
    {
        Clear();
        _source.Dispose();
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
