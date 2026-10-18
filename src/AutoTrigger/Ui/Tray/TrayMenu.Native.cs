using System;
using System.Runtime.InteropServices;

namespace AutoTrigger.Ui
{
    internal sealed partial class TrayMenu
    {
        private static class Win32
        {
            public const uint WS_POPUP = 0x80000000;
            public const int WS_EX_TOOLWINDOW = 0x00000080;

            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOZORDER = 0x0004;
            public const uint SWP_NOACTIVATE = 0x0010;
            public const uint SWP_SHOWWINDOW = 0x0040;
            public const int SW_HIDE = 0;

            public const uint WM_NULL = 0x0000;
            public const int WM_DRAWITEM = 0x002B;
            public const int WM_MEASUREITEM = 0x002C;
            public const int WM_MENUCHAR = 0x0120;
            public const int WM_ENTERIDLE = 0x0121;
            public const int WM_DISPLAYCHANGE = 0x007E;
            public const int WM_DPICHANGED = 0x02E0;
            public const uint WM_RBUTTONDOWN = 0x0204;
            public const uint WM_RBUTTONUP = 0x0205;
            public const uint WM_RBUTTONDBLCLK = 0x0206;
            public const uint WM_NCRBUTTONDOWN = 0x00A4;
            public const uint WM_NCRBUTTONUP = 0x00A5;
            public const uint WM_NCRBUTTONDBLCLK = 0x00A6;

            public const int MSGF_MENU = 2;
            public const int WH_MSGFILTER = -1;
            public const int MNC_EXECUTE = 2;

            public const uint MF_CHECKED = 0x0008;
            public const uint MF_OWNERDRAW = 0x0100;
            public const uint MIM_BACKGROUND = 0x0002;
            public const uint MIM_STYLE = 0x0010;
            public const uint MNS_NOCHECK = 0x80000000;

            public const uint TPM_LEFTALIGN = 0x0000;
            public const uint TPM_RIGHTALIGN = 0x0008;
            public const uint TPM_TOPALIGN = 0x0000;
            public const uint TPM_BOTTOMALIGN = 0x0020;
            public const uint TPM_RETURNCMD = 0x0100;

            public const uint ODT_MENU = 1;
            public const uint ODS_SELECTED = 0x0001;
            public const uint ODS_GRAYED = 0x0002;
            public const uint ODS_DISABLED = 0x0004;
            public const uint ODS_CHECKED = 0x0008;

            public const uint MSAA_MENU_SIG = 0xAA0DF00D;

            public const int DC_BRUSH = 18;
            public const int TRANSPARENT = 1;
            public const int PS_SOLID = 0;
            public const uint DT_LEFT = 0x0000;
            public const uint DT_VCENTER = 0x0004;
            public const uint DT_SINGLELINE = 0x0020;
            public const uint DT_NOPREFIX = 0x0800;
            public const int LOGPIXELSX = 88;

            public const uint SPI_GETNONCLIENTMETRICS = 0x0029;
            public const uint SPI_GETHIGHCONTRAST = 0x0042;
            public const uint HCF_HIGHCONTRASTON = 0x0001;
            public const int COLOR_MENU = 4;
            public const int COLOR_MENUTEXT = 7;
            public const int COLOR_HIGHLIGHT = 13;
            public const int COLOR_HIGHLIGHTTEXT = 14;
            public const int COLOR_GRAYTEXT = 17;

            public const uint MONITOR_DEFAULTTONEAREST = 2;
            public const int MDT_EFFECTIVE_DPI = 0;
            public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            public const int DWMWCP_ROUNDSMALL = 3;

            public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

            public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct SIZE
            {
                public int cx;
                public int cy;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MSG
            {
                public IntPtr hwnd;
                public uint message;
                public IntPtr wParam;
                public IntPtr lParam;
                public uint time;
                public POINT pt;
                public uint lPrivate;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MENUINFO
            {
                public uint cbSize;
                public uint fMask;
                public uint dwStyle;
                public uint cyMax;
                public IntPtr hbrBack;
                public uint dwContextHelpID;
                public UIntPtr dwMenuData;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MEASUREITEMSTRUCT
            {
                public uint CtlType;
                public uint CtlID;
                public uint itemID;
                public uint itemWidth;
                public uint itemHeight;
                public UIntPtr itemData;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct DRAWITEMSTRUCT
            {
                public uint CtlType;
                public uint CtlID;
                public uint itemID;
                public uint itemAction;
                public uint itemState;
                public IntPtr hwndItem;
                public IntPtr hDC;
                public RECT rcItem;
                public UIntPtr itemData;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MSAAMENUINFO
            {
                public uint dwMSAASignature;
                public uint cchWText;
                public IntPtr pszWText;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct HIGHCONTRAST
            {
                public uint cbSize;
                public uint dwFlags;
                public IntPtr lpszDefaultScheme;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct LOGFONTW
            {
                public int lfHeight;
                public int lfWidth;
                public int lfEscapement;
                public int lfOrientation;
                public int lfWeight;
                public byte lfItalic;
                public byte lfUnderline;
                public byte lfStrikeOut;
                public byte lfCharSet;
                public byte lfOutPrecision;
                public byte lfClipPrecision;
                public byte lfQuality;
                public byte lfPitchAndFamily;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string lfFaceName;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct NONCLIENTMETRICSW
            {
                public uint cbSize;
                public int iBorderWidth;
                public int iScrollWidth;
                public int iScrollHeight;
                public int iCaptionWidth;
                public int iCaptionHeight;
                public LOGFONTW lfCaptionFont;
                public int iSmCaptionWidth;
                public int iSmCaptionHeight;
                public LOGFONTW lfSmCaptionFont;
                public int iMenuWidth;
                public int iMenuHeight;
                public LOGFONTW lfMenuFont;
                public LOGFONTW lfStatusFont;
                public LOGFONTW lfMessageFont;
                public int iPaddedBorderWidth;
            }

            [DllImport("user32.dll")]
            public static extern IntPtr CreatePopupMenu();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DestroyMenu(IntPtr menu);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, IntPtr item);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetMenuInfo(IntPtr menu, ref MENUINFO info);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

            [DllImport("user32.dll")]
            public static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EndMenu();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hwnd, int command);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hwnd);

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindowVisible(IntPtr hwnd);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
            public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr lParam);

            [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
            public static extern IntPtr SetWindowsHookEx(int hookType, HookProc proc, IntPtr module, uint threadId);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(IntPtr hook);

            [DllImport("user32.dll")]
            public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

            [DllImport("user32.dll")]
            public static extern IntPtr GetDC(IntPtr hwnd);

            [DllImport("user32.dll")]
            public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

            [DllImport("user32.dll")]
            public static extern int FillRect(IntPtr dc, ref RECT rect, IntPtr brush);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DrawTextW")]
            public static extern int DrawText(IntPtr dc, string text, int count, ref RECT rect, uint format);

            [DllImport("user32.dll")]
            public static extern uint GetSysColor(int index);

            [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SystemParametersInfo(uint action, uint param, ref HIGHCONTRAST value, uint winIni);

            [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SystemParametersInfo(uint action, uint param, ref NONCLIENTMETRICSW value, uint winIni);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SystemParametersInfoForDpi(uint action, uint param, ref NONCLIENTMETRICSW value, uint winIni, uint dpi);

            [DllImport("kernel32.dll")]
            public static extern uint GetCurrentThreadId();

            [DllImport("shcore.dll")]
            public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

            [DllImport("dwmapi.dll")]
            public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateSolidBrush(uint color);

            [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFontIndirectW")]
            public static extern IntPtr CreateFontIndirect(ref LOGFONTW font);

            [DllImport("gdi32.dll")]
            public static extern IntPtr CreatePen(int style, int width, uint color);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteObject(IntPtr gdiObject);

            [DllImport("gdi32.dll")]
            public static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

            [DllImport("gdi32.dll")]
            public static extern IntPtr GetStockObject(int index);

            [DllImport("gdi32.dll")]
            public static extern uint SetDCBrushColor(IntPtr dc, uint color);

            [DllImport("gdi32.dll")]
            public static extern int SetBkMode(IntPtr dc, int mode);

            [DllImport("gdi32.dll")]
            public static extern uint SetTextColor(IntPtr dc, uint color);

            [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextExtentPoint32W")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetTextExtentPoint32(IntPtr dc, string text, int count, out SIZE size);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool Polyline(IntPtr dc, POINT[] points, int count);

            [DllImport("gdi32.dll")]
            public static extern int SaveDC(IntPtr dc);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RestoreDC(IntPtr dc, int saved);

            [DllImport("gdi32.dll")]
            public static extern int GetDeviceCaps(IntPtr dc, int index);
        }
    }
}
