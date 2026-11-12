using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CPUSTATS;

[SupportedOSPlatform("windows")]
internal sealed class WindowsTray : IDisposable
{
    private const uint TrayMessage = 0x8001;
    private const uint AddIcon = 0, DeleteIcon = 2, SetIconVersion = 4;
    private const uint MenuSeparator = 0x800, MenuPopup = 0x10, MenuChecked = 0x8, MenuDisabled = 0x3;
    private static bool _systemMenuTheme;
    private readonly NativeMenu _menu;
    private readonly Action _open;
    private readonly SubclassProc _callback;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private NotifyIconData _data;

    private WindowsTray(nint window, NativeMenu menu, Action open, nint icon)
    {
        _menu = menu;
        _open = open;
        _callback = OnMessage;
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 1,
            Flags = 0x87, Callback = TrayMessage, Icon = icon, Tip = "CPUSTATS",
            Info = "", InfoTitle = ""
        };
    }

    public static WindowsTray? TryCreate(Window window, NativeMenu menu, Action open)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == 0 ||
            ExtractIconEx(Environment.ProcessPath!, 0, 0, out var icon, 1) == 0)
            return null;

        var tray = new WindowsTray(handle, menu, open, icon);
        if (SetWindowSubclass(handle, tray._callback, 1, 0) && tray.RegisterIcon())
            return tray;
        tray.Dispose();
        return null;
    }

    internal static void UseSystemMenuTheme()
    {
        // The native menu theme entry points use ordinals and changed in Windows 10 1903.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362)) return;
        try
        {
            SetPreferredAppMode(1); // AllowDark, following the Windows app theme.
            _systemMenuTheme = true;
        }
        catch (EntryPointNotFoundException) { }
    }

    private bool RegisterIcon()
    {
        if (!ShellNotifyIcon(AddIcon, ref _data)) return false;
        _data.Version = 4;
        ShellNotifyIcon(SetIconVersion, ref _data);
        return true;
    }

    private nint OnMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == _taskbarCreated) RegisterIcon();
        if (message == TrayMessage)
        {
            switch ((uint)lParam & 0xffff)
            {
                case 0x007B: // WM_CONTEXTMENU
                    ShowMenu(unchecked((short)wParam), unchecked((short)(wParam >> 16)));
                    return 0;
                case 0x0205: // WM_RBUTTONUP (legacy notification protocol)
                    if (GetCursorPos(out var point)) ShowMenu(point.X, point.Y);
                    return 0;
                case 0x0202: // WM_LBUTTONUP
                case 0x0400: // NIN_SELECT
                case 0x0401: // NIN_KEYSELECT
                    Dispatcher.UIThread.Post(_open);
                    return 0;
            }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        if (_systemMenuTheme)
        {
            try { FlushMenuThemes(); }
            catch (EntryPointNotFoundException) { _systemMenuTheme = false; }
        }
        var commands = new List<NativeMenuItem>();
        var popup = BuildMenu(_menu, commands);
        if (popup == 0) return;
        try
        {
            SetForegroundWindow(_data.Window);
            uint selected = TrackPopupMenu(popup, 0x0102, x, y, 0, _data.Window, 0);
            PostMessage(_data.Window, 0, 0, 0);
            if (selected > 0 && selected <= commands.Count)
            {
                var item = (INativeMenuItemExporterEventsImplBridge)commands[(int)selected - 1];
                Dispatcher.UIThread.Post(item.RaiseClicked);
            }
        }
        finally { DestroyMenu(popup); }
    }

    private static nint BuildMenu(NativeMenu source, List<NativeMenuItem> commands)
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return 0;
        foreach (var entry in source.Items)
        {
            if (entry is NativeMenuItemSeparator)
            {
                AppendMenu(menu, MenuSeparator, 0, null);
                continue;
            }
            if (entry is not NativeMenuItem item || !item.IsVisible) continue;
            uint flags = item.IsEnabled ? 0u : MenuDisabled;
            if (item.Menu is { } children)
            {
                var submenu = BuildMenu(children, commands);
                if (submenu != 0 && !AppendMenu(menu, flags | MenuPopup, (nuint)submenu, item.Header))
                    DestroyMenu(submenu);
                continue;
            }
            commands.Add(item);
            AppendMenu(menu, flags | (item.IsChecked ? MenuChecked : 0u), (nuint)commands.Count, item.Header);
        }
        return menu;
    }

    public void Dispose()
    {
        ShellNotifyIcon(DeleteIcon, ref _data);
        RemoveWindowSubclass(_data.Window, _callback, 1);
        DestroyIcon(_data.Icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, nint large, out nint small, uint count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
}
