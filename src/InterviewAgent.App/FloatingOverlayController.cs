using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using InterviewAgent.App.ViewModels;
using InterviewAgent.Core.Overlay;

namespace InterviewAgent.App;

internal sealed class FloatingOverlayController : IAsyncDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly MainViewModel _viewModel;
    private readonly Func<Task> _toggleCapture;
    private FloatingOverlayWindow? _ballWindow;
    private ConversationBubbleWindow? _bubbleWindow;
    private Task _pendingPlacementSave = Task.CompletedTask;
    private bool _disposed;

    public FloatingOverlayController(
        MainWindow mainWindow,
        MainViewModel viewModel,
        Func<Task> toggleCapture)
    {
        _mainWindow = mainWindow ??
            throw new ArgumentNullException(nameof(mainWindow));
        _viewModel = viewModel ??
            throw new ArgumentNullException(nameof(viewModel));
        _toggleCapture = toggleCapture ??
            throw new ArgumentNullException(nameof(toggleCapture));
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (enabled)
        {
            ShowWindows();
            return;
        }

        await CloseWindowsAsync(savePlacement: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await CloseWindowsAsync(savePlacement: true);
        await _pendingPlacementSave;
    }

    private void ShowWindows()
    {
        if (_ballWindow is not null)
        {
            UpdateBubbleVisibility();
            return;
        }

        _ballWindow = new FloatingOverlayWindow(
            _viewModel,
            _viewModel.Settings,
            _toggleCapture,
            RestoreMainWindow);
        _bubbleWindow = new ConversationBubbleWindow(_viewModel);
        _ballWindow.PlacementChanged += OnBallPlacementChanged;
        _ballWindow.PlacementCommitted += OnBallPlacementCommitted;
        _bubbleWindow.SizeChanged += OnBubbleSizeChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _ballWindow.Show();
        UpdateBubbleVisibility();
    }

    private async Task CloseWindowsAsync(bool savePlacement)
    {
        var ball = _ballWindow;
        var bubble = _bubbleWindow;
        if (ball is null)
        {
            return;
        }

        if (savePlacement)
        {
            QueuePlacementSave(ball.CurrentPlacement);
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ball.PlacementChanged -= OnBallPlacementChanged;
        ball.PlacementCommitted -= OnBallPlacementCommitted;
        if (bubble is not null)
        {
            bubble.SizeChanged -= OnBubbleSizeChanged;
            bubble.Close();
        }

        ball.Close();
        _bubbleWindow = null;
        _ballWindow = null;
        await _pendingPlacementSave;
    }

    private void OnBallPlacementChanged(object? sender, EventArgs e) =>
        PositionBubble();

    private void OnBallPlacementCommitted(
        object? sender,
        FloatingOverlayPlacementEventArgs e) =>
        QueuePlacementSave(e.Placement);

    private void OnBubbleSizeChanged(object sender, SizeChangedEventArgs e) =>
        PositionBubble();

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsOverlayConversationVisible))
        {
            UpdateBubbleVisibility();
        }
    }

    private void UpdateBubbleVisibility()
    {
        var bubble = _bubbleWindow;
        if (bubble is null)
        {
            return;
        }

        if (!_viewModel.IsOverlayConversationVisible)
        {
            if (bubble.IsVisible)
            {
                bubble.Hide();
            }

            return;
        }

        if (!bubble.IsVisible)
        {
            bubble.Show();
        }

        _ = bubble.Dispatcher.InvokeAsync(
            () =>
            {
                bubble.UpdateLayout();
                PositionBubble();
            },
            DispatcherPriority.Loaded);
    }

    private void PositionBubble()
    {
        var ball = _ballWindow;
        var bubble = _bubbleWindow;
        if (ball is null || bubble is null || !bubble.IsVisible)
        {
            return;
        }

        var width = double.IsFinite(bubble.ActualWidth) && bubble.ActualWidth > 0
            ? bubble.ActualWidth
            : bubble.Width;
        var height = double.IsFinite(bubble.ActualHeight) && bubble.ActualHeight > 0
            ? bubble.ActualHeight
            : 100;
        var placement = FloatingOverlayLayout.PositionBubble(
            ball.Left,
            ball.Top,
            ball.Width,
            width,
            height,
            OverlayWindowInterop.GetWorkArea(ball));
        bubble.Left = placement.Left;
        bubble.Top = placement.Top;
    }

    private void QueuePlacementSave(FloatingOverlayPlacement placement)
    {
        _pendingPlacementSave = SaveAfterPreviousAsync(
            _pendingPlacementSave,
            placement);
    }

    private async Task SaveAfterPreviousAsync(
        Task previous,
        FloatingOverlayPlacement placement)
    {
        try
        {
            await previous;
            await _viewModel.SaveOverlayPlacementAsync(
                placement.Left,
                placement.Top,
                placement.Diameter);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _viewModel.ReportUiError("悬浮球位置保存失败，本次使用不受影响。");
        }
    }

    private void RestoreMainWindow()
    {
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        var wasTopmost = _mainWindow.Topmost;
        _mainWindow.Topmost = true;
        _mainWindow.Activate();
        _mainWindow.Topmost = wasTopmost;
    }
}
