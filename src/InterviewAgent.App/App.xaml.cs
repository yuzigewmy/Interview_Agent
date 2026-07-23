using System.Windows;

namespace InterviewAgent.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                "应用遇到未处理错误。请重试；诊断日志不包含音频、问题正文或密钥。",
                "Interview Agent",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = new MainWindow(new AppRuntime());
        MainWindow = window;
        window.Show();
    }
}

