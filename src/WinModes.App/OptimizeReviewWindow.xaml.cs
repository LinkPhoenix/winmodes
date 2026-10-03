using System.Windows;

namespace WinModes.App;

internal sealed record OptimizeReviewItem(string Title, string Label, string Target, string Transition, string Scope, string Recovery, string Warning);

public partial class OptimizeReviewWindow : Wpf.Ui.Controls.FluentWindow
{
    internal OptimizeReviewWindow(IReadOnlyList<OptimizeReviewItem> changes, string summary)
    {
        InitializeComponent();
        Summary.Text = summary;
        Changes.ItemsSource = changes;
        Loaded += (_, _) =>
        {
            MaxWidth = SystemParameters.WorkArea.Width;
            MaxHeight = SystemParameters.WorkArea.Height;
            CancelButton.Focus();
        };
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
