using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using InterviewAgent.App.ViewModels;

namespace InterviewAgent.App;

public partial class ConversationBubbleWindow : Window
{
    private readonly MainViewModel _viewModel;

    public ConversationBubbleWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        OverlayWindowInterop.ApplyNoActivateStyle(this);

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
                nameof(MainViewModel.OverlayQuestion) or
                nameof(MainViewModel.OverlayAnswer)))
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(
            () =>
            {
                QuestionScrollViewer.ScrollToEnd();
                AnswerScrollViewer.ScrollToEnd();
            },
            DispatcherPriority.Loaded);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }
}
