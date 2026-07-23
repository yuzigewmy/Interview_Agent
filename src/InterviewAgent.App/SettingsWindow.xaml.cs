using System.Windows;
using InterviewAgent.Core.Settings;

namespace InterviewAgent.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _current;

    public SettingsWindow(AppSettings current)
    {
        _current = current ?? throw new ArgumentNullException(nameof(current));
        InitializeComponent();
        DemoModeCheckBox.IsChecked = current.DemoMode;
        ResourceIdTextBox.Text = current.VolcanoResourceId;
        DeepSeekSavedLabel.Text = current.HasDeepSeekCredential
            ? "已保存凭据"
            : "尚未配置";
        VolcanoSavedLabel.Text = current.HasVolcanoCredential
            ? "已保存凭据"
            : "尚未配置";
    }

    public AppSettings? ResultSettings { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var clear = ClearCredentialsCheckBox.IsChecked == true;
            var settings = new AppSettings(
                DemoMode: DemoModeCheckBox.IsChecked == true,
                DeepSeekApiKey: ResolveSecret(
                    DeepSeekPasswordBox.Password,
                    _current.DeepSeekApiKey,
                    clear),
                VolcanoApiKey: ResolveSecret(
                    VolcanoApiKeyPasswordBox.Password,
                    _current.VolcanoApiKey,
                    clear),
                VolcanoAppKey: ResolveSecret(
                    VolcanoAppKeyPasswordBox.Password,
                    _current.VolcanoAppKey,
                    clear),
                VolcanoAccessKey: ResolveSecret(
                    VolcanoAccessKeyPasswordBox.Password,
                    _current.VolcanoAccessKey,
                    clear),
                VolcanoResourceId: ResourceIdTextBox.Text.Trim());
            settings.Validate();
            if (!settings.DemoMode && !settings.HasDeepSeekCredential)
            {
                throw new InvalidOperationException(
                    "关闭演示模式前，请先配置 DeepSeek API Key。");
            }

            ResultSettings = settings;
            DialogResult = true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            ValidationMessage.Text = exception.Message;
        }
    }

    private static string ResolveSecret(
        string entered,
        string existing,
        bool clear) =>
        clear
            ? string.Empty
            : string.IsNullOrEmpty(entered)
                ? existing
                : entered.Trim();
}

