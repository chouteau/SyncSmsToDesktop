using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace SmsSyncMaui.WinUI
{
    public static class TrayService
    {
        private static Microsoft.UI.Xaml.Window? _uiWindow;
        private static AppWindow? _appWindow;
        private static IntPtr _hWnd;
        private static bool _isReallyClosing;
        private static NOTIFYICONDATAW _nid;
        private static SubclassProc? _subclassProc;
        private static uint _wmTaskbarCreated;

        public const uint WM_APP = 0x8000;
        public const uint WM_TRAYICON = WM_APP + 1;

        public const uint NIM_ADD = 0x00000000;
        public const uint NIM_MODIFY = 0x00000001;
        public const uint NIM_DELETE = 0x00000002;

        public const uint NIF_MESSAGE = 0x00000001;
        public const uint NIF_ICON = 0x00000002;
        public const uint NIF_TIP = 0x00000004;

        public const uint WM_LBUTTONUP = 0x0202;
        public const uint WM_LBUTTONDBLCLK = 0x0203;
        public const uint WM_RBUTTONUP = 0x0205;
        
        public const uint WM_NULL = 0x0000;
        public const uint TPM_RETURNCMD = 0x0100;
        public const uint TPM_LEFTALIGN = 0x0000;
        public const uint TPM_RIGHTBUTTON = 0x0002;
        public const uint MF_STRING = 0x00000000;

        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;

        public const uint WM_GETICON = 0x007F;
        public const int ICON_BIG = 1;
        public const int GCLP_HICON = -14;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATAW
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
        public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc callback, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
        public static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc callback, IntPtr uIdSubclass);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW pnid);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        public static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        public static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
        public static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        public static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        public static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

        public static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetClassLongPtr64(hWnd, nIndex);
            else
                return new IntPtr(GetClassLong32(hWnd, nIndex));
        }

        public static void Initialize(Microsoft.UI.Xaml.Window window)
        {
            _uiWindow = window;
            _hWnd = WindowNative.GetWindowHandle(window);
            _isReallyClosing = false;

            _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hWnd);
            _appWindow = AppWindow.GetFromWindowId(id);

            _appWindow.Closing += (sender, args) =>
            {
                if (!_isReallyClosing)
                {
                    args.Cancel = true;
                    _appWindow.Hide();
                }
            };

            // Register subclass
            _subclassProc = new SubclassProc(WindowSubclassCallback);
            SetWindowSubclass(_hWnd, _subclassProc, (IntPtr)1, IntPtr.Zero);

            // Get icon
            IntPtr hIcon = SendMessage(_hWnd, WM_GETICON, (IntPtr)1, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
            {
                hIcon = GetClassLongPtr(_hWnd, GCLP_HICON);
            }
            if (hIcon == IntPtr.Zero)
            {
                hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
            }

            // Create notification data
            _nid = new NOTIFYICONDATAW();
            _nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>();
            _nid.hWnd = _hWnd;
            _nid.uID = 1;
            _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            _nid.uCallbackMessage = WM_TRAYICON;
            _nid.hIcon = hIcon;
            _nid.szTip = "WinSms (SmsSync)";

            Shell_NotifyIconW(NIM_ADD, ref _nid);
        }

        public static void ReallyQuit()
        {
            _isReallyClosing = true;
            Shell_NotifyIconW(NIM_DELETE, ref _nid);
            if (_subclassProc != null)
            {
                RemoveWindowSubclass(_hWnd, _subclassProc, (IntPtr)1);
            }
            _uiWindow?.Close();
            
            // Just in case MAUI lifecycle requires a harder exit
            Microsoft.Maui.Controls.Application.Current?.Quit();
        }

        public static void RestoreWindow()
        {
            if (_uiWindow != null)
            {
                _appWindow?.Show();
                ShowWindow(_hWnd, SW_RESTORE);
                SetForegroundWindow(_hWnd);
            }
        }

        private static IntPtr WindowSubclassCallback(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_TRAYICON)
            {
                uint msgType = (uint)lParam.ToInt64();

                if (msgType == WM_LBUTTONUP || msgType == WM_LBUTTONDBLCLK)
                {
                    RestoreWindow();
                    return IntPtr.Zero;
                }
                else if (msgType == WM_RBUTTONUP)
                {
                    ShowContextMenu(hWnd);
                    return IntPtr.Zero;
                }
            }
            else if (uMsg == _wmTaskbarCreated)
            {
                // Re-add tray icon if Windows Explorer restarts
                Shell_NotifyIconW(NIM_ADD, ref _nid);
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private static void ShowContextMenu(IntPtr hWnd)
        {
            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            AppendMenuW(hMenu, MF_STRING, (IntPtr)1, "Ouvrir");
            AppendMenuW(hMenu, MF_STRING, (IntPtr)2, "Quitter");

            if (GetCursorPos(out POINT pt))
            {
                SetForegroundWindow(hWnd);
                int selectedCmd = TrackPopupMenu(hMenu, TPM_LEFTALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, hWnd, IntPtr.Zero);
                DestroyMenu(hMenu);

                PostMessageW(hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

                if (selectedCmd == 1)
                {
                    RestoreWindow();
                }
                else if (selectedCmd == 2)
                {
                    ReallyQuit();
                }
            }
            else
            {
                DestroyMenu(hMenu);
            }
        }
    }
}
