using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using InterviewAgent.App.ViewModels;
using InterviewAgent.Core.Overlay;
using InterviewAgent.Core.Settings;
using Microsoft.Win32;

namespace InterviewAgent.App;

public partial class FloatingOverlayWindow : Window
{
    private const double DefaultScreenOffset = 24;
    private readonly Func<Task> _toggleCapture;
    private readonly Action _restoreMainWindow;
    private readonly AppSettings _initialSettings;
    private readonly DispatcherTimer _singleClickTimer;
    private Point _dragStartCursor;
    private double _dragStartLeft;
    private double _dragStartTop;
    private bool _ballPointerActive;
    private bool _isDragging;
    private bool _ignoreNextMouseUp;

    public FloatingOverlayWindow(
        MainViewModel viewModel,
        AppSettings initialSettings,
        Func<Task> toggleCapture,
        Action restoreMainWindow)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _initialSettings = initialSettings ??
            throw new ArgumentNullException(nameof(initialSettings));
        _toggleCapture = toggleCapture ??
            throw new ArgumentNullException(nameof(toggleCapture));
        _restoreMainWindow = restoreMainWindow ??
            throw new ArgumentNullException(nameof(restoreMainWindow));

        InitializeComponent();
        DataContext = viewModel;
        Width = FloatingOverlayLayout.NormalizeDiameter(
            initialSettings.FloatingOverlayDiameter);
        Height = Width;
        _singleClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()),
        };
        _singleClickTimer.Tick += OnSingleClickTimerTick;
    }

    public event EventHandler? PlacementChanged;

    public event EventHandler<FloatingOverlayPlacementEventArgs>? PlacementCommitted;

    public FloatingOverlayPlacement CurrentPlacement =>
        new(Left, Top, Width);

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        OverlayWindowInterop.ApplyNoActivateStyle(this);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = OverlayWindowInterop.GetWorkArea(this);
        Left = _initialSettings.FloatingOverlayLeft ??
            workArea.Right - Width - DefaultScreenOffset;
        Top = _initialSettings.FloatingOverlayTop ??
            workArea.Bottom - Height - DefaultScreenOffset;
        ClampToVisibleWorkArea();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _singleClickTimer.Stop();
        _singleClickTimer.Tick -= OnSingleClickTimerTick;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnBallMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || ResizeThumb.IsMouseOver)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _singleClickTimer.Stop();
            _ignoreNextMouseUp = true;
            _ballPointerActive = false;
            ReleaseMouseCapture();
            _restoreMainWindow();
            e.Handled = true;
            return;
        }

        _dragStartCursor = OverlayWindowInterop.GetCursorPosition(this);
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _ballPointerActive = true;
        _isDragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnBallMouseMove(object sender, MouseEventArgs e)
    {
        if (!_ballPointerActive ||
            !IsMouseCaptured ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var cursor = OverlayWindowInterop.GetCursorPosition(this);
        var horizontalChange = cursor.X - _dragStartCursor.X;
        var verticalChange = cursor.Y - _dragStartCursor.Y;
        if (!_isDragging &&
            Math.Abs(horizontalChange) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(verticalChange) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        Left = _dragStartLeft + horizontalChange;
        Top = _dragStartTop + verticalChange;
        PlacementChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnBallMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_ballPointerActive)
        {
            return;
        }

        _ballPointerActive = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (_ignoreNextMouseUp)
        {
            _ignoreNextMouseUp = false;
            e.Handled = true;
            return;
        }

        if (_isDragging)
        {
            ClampToVisibleWorkArea();
            CommitPlacement();
        }
        else
        {
            _singleClickTimer.Stop();
            _singleClickTimer.Start();
        }

        e.Handled = true;
    }

    private void OnSingleClickTimerTick(object? sender, EventArgs e)
    {
        _singleClickTimer.Stop();
        _ = ToggleCaptureSafeAsync();
    }

    private async Task ToggleCaptureSafeAsync()
    {
        try
        {
            await _toggleCapture();
        }
        catch
        {
            // The main window callback reports a safe user-facing error.
        }
    }

    private void OnResizeDragDelta(
        object sender,
        DragDeltaEventArgs e)
    {
        var delta = Math.Abs(e.HorizontalChange) >= Math.Abs(e.VerticalChange)
            ? e.HorizontalChange
            : e.VerticalChange;
        var diameter = FloatingOverlayLayout.NormalizeDiameter(Width + delta);
        Width = diameter;
        Height = diameter;
        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnResizeDragCompleted(
        object sender,
        DragCompletedEventArgs e)
    {
        ClampToVisibleWorkArea();
        CommitPlacement();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            ClampToVisibleWorkArea();
            CommitPlacement();
        });
    }

    private void ClampToVisibleWorkArea()
    {
        var placement = FloatingOverlayLayout.ClampBallToWorkArea(
            Left,
            Top,
            Width,
            OverlayWindowInterop.GetWorkArea(this));
        Left = placement.Left;
        Top = placement.Top;
        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CommitPlacement()
    {
        PlacementChanged?.Invoke(this, EventArgs.Empty);
        PlacementCommitted?.Invoke(
            this,
            new FloatingOverlayPlacementEventArgs(CurrentPlacement));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}

public sealed record FloatingOverlayPlacement(
    double Left,
    double Top,
    double Diameter);

public sealed class FloatingOverlayPlacementEventArgs(
    FloatingOverlayPlacement placement) : EventArgs
{
    public FloatingOverlayPlacement Placement { get; } = placement;
}
