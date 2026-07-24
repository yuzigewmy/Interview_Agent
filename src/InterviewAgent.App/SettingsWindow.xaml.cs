using System.Globalization;
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
        ProviderTextBox.Text = current.ModelProvider;
        ModelNameTextBox.Text = current.ModelName;
        ModelBaseUrlTextBox.Text = current.ModelBaseUrl;
        ModelMaxTokensTextBox.Text = current.ModelMaxTokens.ToString(
            CultureInfo.InvariantCulture);
        ResourceIdTextBox.Text = current.VolcanoResourceId;
        ModelSavedLabel.Text = string.IsNullOrWhiteSpace(current.ModelApiKey)
            ? "API Key 未配置（本地模型可留空）"
            : "已保存 API Key";
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
            if (!int.TryParse(
                    ModelMaxTokensTextBox.Text.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var modelMaxTokens))
            {
                throw new ArgumentException(
                    "最大输出 Token 必须是 64 到 8192 之间的整数。");
            }

            var settings = new AppSettings(
                DemoMode: DemoModeCheckBox.IsChecked == true,
                ModelProvider: ProviderTextBox.Text.Trim(),
                ModelName: ModelNameTextBox.Text.Trim(),
                ModelBaseUrl: ModelBaseUrlTextBox.Text.Trim(),
                ModelApiKey: ResolveSecret(
                    ModelApiKeyPasswordBox.Password,
                    _current.ModelApiKey,
                    clear),
                ModelMaxTokens: modelMaxTokens,
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
