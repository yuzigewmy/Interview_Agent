using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using InterviewAgent.App.ViewModels;
using Microsoft.Win32;

namespace InterviewAgent.App;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x4941;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyF8 = 0x77;
    private readonly MainViewModel _viewModel;
    private HwndSource? _windowSource;

    public MainWindow(AppRuntime runtime)
    {
        InitializeComponent();
        _viewModel = new MainViewModel(runtime);
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch
        {
            _viewModel.ReportUiError("初始化失败，请关闭后重新打开应用。");
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowProcedure);
        if (!NativeMethods.RegisterHotKey(handle, HotkeyId, ModNoRepeat, VirtualKeyF8))
        {
            _viewModel.ReportUiError("全局 F8 已被其他应用占用，可使用界面按钮控制采集。");
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.UnregisterHotKey(handle, HotkeyId);
        _windowSource?.RemoveHook(WindowProcedure);
        await _viewModel.DisposeAsync();
    }

    private async void OnToggleCaptureClick(object sender, RoutedEventArgs e) =>
        await ToggleCaptureSafeAsync();

    private async void OnAskClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.AskAsync();
        }
        catch
        {
            _viewModel.ReportUiError("生成回答失败，请检查问题或设置。");
        }
    }

    private async void OnRefreshAudioDevicesClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshAudioDevicesAsync();
        }
        catch
        {
            _viewModel.ReportUiError("刷新音频来源失败，请检查设备连接状态。");
        }
    }

    private async void OnExportInterviewClick(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出面试问答记录",
            Filter = "Word 文档 (*.docx)|*.docx",
            DefaultExt = ".docx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = _viewModel.CreateSuggestedExportFileName(
                DateTimeOffset.Now),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _viewModel.ExportInterviewAsync(dialog.FileName);
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidOperationException or
                  IOException or
                  UnauthorizedAccessException)
        {
            _viewModel.ReportUiError(
                "导出失败，请确认目标文件未被占用且目录可写。");
        }
        catch
        {
            _viewModel.ReportUiError("导出失败，请稍后重试。");
        }
    }

    private async void OnImportKnowledgeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择面经知识库",
            Filter = "Word 文档 (*.docx)|*.docx",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.ImportKnowledgeAsync(dialog.FileName);
        }
    }

    private async void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_viewModel.Settings)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true && dialog.ResultSettings is not null)
        {
            try
            {
                await _viewModel.SaveSettingsAsync(dialog.ResultSettings);
            }
            catch (Exception exception)
                when (exception is ArgumentException or InvalidOperationException)
            {
                _viewModel.ReportUiError(exception.Message);
            }
        }
    }

    private void OnTopmostClick(object sender, RoutedEventArgs e) =>
        Topmost = TopmostCheckBox.IsChecked == true;

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message == WmHotkey && wordParameter.ToInt32() == HotkeyId)
        {
            handled = true;
            _ = ToggleCaptureSafeAsync();
        }

        return IntPtr.Zero;
    }

    private async Task ToggleCaptureSafeAsync()
    {
        try
        {
            await _viewModel.ToggleCaptureAsync();
        }
        catch
        {
            _viewModel.ReportUiError("切换音频采集失败，请检查设备状态。");
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(
            IntPtr windowHandle,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
    }
}
