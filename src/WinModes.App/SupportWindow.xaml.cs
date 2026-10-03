using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Wpf.Ui.Controls;

namespace WinModes.App;

/// <summary>Optional support for the developer, paid through the external PayPal page.</summary>
public partial class SupportWindow : FluentWindow
{
    private const string DonationUrl = "https://paypal.me/EmilioLECERF";

    public SupportWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Keep the dialog usable on smaller screens and at larger text scaling.
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
        };
    }

    private void OnDonate(object sender, RoutedEventArgs e)
    {
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(DonationUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ErrorText.Text = Loc.T("Your browser could not be opened. Visit paypal.me/EmilioLECERF to donate.");
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
