using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.Graphics;

namespace DiscBg.Gui;

sealed partial class SquareLock
{
    const uint WM_SIZING = 0x0214, WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
    const int Left = 1, Right = 2, Top = 3, TopLeft = 4, TopRight = 5, Bottom = 6, BottomLeft = 7;
    // DIPs: the smallest stage
    const double MinSide = 320;

    readonly Func<Size?> chrome;
    readonly Action dragEnded;
    // Kept alive for as long as the window uses it
    readonly SubclassProc proc;
    double dragStartSide;

    public SquareLock(IntPtr hwnd, Func<Size?> chrome, Action dragEnded)
    {
        this.chrome = chrome;
        this.dragEnded = dragEnded;
        proc = WndProc;
        SetWindowSubclass(hwnd, proc, 1, 0);
    }

    IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (msg == WM_EXITSIZEMOVE) dragEnded();
        if (msg is not (WM_SIZING or WM_ENTERSIZEMOVE) || chrome() is not { } inner)
            return DefSubclassProc(hwnd, msg, wParam, lParam);

        double scale = GetDpiForWindow(hwnd) / 96.0;
        GetWindowRect(hwnd, out var outer);
        GetClientRect(hwnd, out var client);
        double chromeW = outer.Width - client.Width + inner.Width * scale;
        double chromeH = outer.Height - client.Height + inner.Height * scale;
        if (msg == WM_ENTERSIZEMOVE)
        {
            dragStartSide = outer.Width - chromeW;
            return DefSubclassProc(hwnd, msg, wParam, lParam);
        }

        var r = Marshal.PtrToStructure<Rect32>(lParam);
        int edge = (int)wParam;
        double fromWidth = r.Width - chromeW, fromHeight = r.Height - chromeH;
        bool byWidth = edge is Left or Right
            || (edge is not (Top or Bottom) && Math.Abs(fromWidth - dragStartSide) >= Math.Abs(fromHeight - dragStartSide));
        double side = byWidth ? fromWidth : fromHeight;
        bool growsLeft = edge is Left or TopLeft or BottomLeft, growsUp = edge is Top or TopLeft or TopRight;
        var work = DisplayArea.GetFromRect(new RectInt32(r.Left, r.Top, r.Width, r.Height), DisplayAreaFallback.Nearest).WorkArea;
        double roomW = (growsLeft ? r.Right - work.X : work.X + work.Width - r.Left) - chromeW;
        double roomH = (growsUp ? r.Bottom - work.Y : work.Y + work.Height - r.Top) - chromeH;
        side = Math.Max(Math.Min(side, Math.Min(roomW, roomH)), MinSide * scale);

        int width = (int)Math.Round(side + chromeW), height = (int)Math.Round(side + chromeH);
        if (growsLeft) r.Left = r.Right - width; else r.Right = r.Left + width;
        if (growsUp) r.Top = r.Bottom - height; else r.Bottom = r.Top + height;
        Marshal.StructureToPtr(r, lParam, false);
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect32
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data);

    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out Rect32 rect);
}
