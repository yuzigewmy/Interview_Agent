using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using InterviewAgent.Core.Overlay;

namespace InterviewAgent.App;

internal static class OverlayWindowInterop
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public static void ApplyNoActivateStyle(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var current = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(
            handle,
            GwlExStyle,
            new IntPtr(current | WsExNoActivate | WsExToolWindow));
    }

    public static OverlayBounds GetWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return FromRect(SystemParameters.WorkArea);
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return FromRect(SystemParameters.WorkArea);
        }

        var dpi = GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1d : dpi / 96d;
        return new OverlayBounds(
            info.WorkArea.Left / scale,
            info.WorkArea.Top / scale,
            (info.WorkArea.Right - info.WorkArea.Left) / scale,
            (info.WorkArea.Bottom - info.WorkArea.Top) / scale);
    }

    public static Point GetCursorPosition(Window window)
    {
        if (!GetCursorPos(out var point))
        {
            return Mouse.GetPosition(window);
        }

        var source = PresentationSource.FromVisual(window);
        return source?.CompositionTarget?.TransformFromDevice.Transform(
                   new Point(point.X, point.Y))
               ?? new Point(point.X, point.Y);
    }

    private static OverlayBounds FromRect(Rect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr windowHandle,
        int index,
        IntPtr newValue);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr windowHandle,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
