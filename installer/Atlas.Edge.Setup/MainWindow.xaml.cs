using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Atlas.Edge.Setup;

public partial class MainWindow : Window
{
    private readonly TextBlock[] _stepLabels;
    private int _stepIndex;

    public MainWindow()
    {
        InitializeComponent();

        var computerName = Environment.MachineName;

        ComputerNameText.Text = computerName;
        ConnectComputerNameText.Text = computerName;
        OperatingSystemText.Text =
            $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

        _stepLabels =
        [
            WelcomeStep,
            ConnectStep,
            InstallStep,
            VerifyStep,
            CompleteStep
        ];

        ShowStep(0);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_stepIndex > 0)
        {
            ShowStep(_stepIndex - 1);
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_stepIndex == 2)
        {
            await RunInstallationAsync();
            return;
        }

        if (_stepIndex < 4)
        {
            ShowStep(_stepIndex + 1);
            return;
        }

        Close();
    }

    private async Task RunInstallationAsync()
    {
        BackButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        NextButton.Content = "Installing...";

        ResetInstallationStatus();

        await UpdateInstallStepAsync(
            RuntimeInstallStatus,
            "Runtime files",
            25,
            "Installing Atlas Edge runtime...");

        await UpdateInstallStepAsync(
            ServiceInstallStatus,
            "Windows service",
            50,
            "Registering the Atlas Edge Windows service...");

        await UpdateInstallStepAsync(
            ConfigurationInstallStatus,
            "Enrollment configuration",
            75,
            "Preparing secure Atlas enrollment...");

        await UpdateInstallStepAsync(
            StartupInstallStatus,
            "Starting Atlas Edge",
            100,
            "Starting the Atlas Edge runtime...");

        InstallDetailText.Text =
            "Installation completed successfully.";

        await Task.Delay(700);

        ShowStep(3);
    }

    private async Task UpdateInstallStepAsync(
        TextBlock statusText,
        string label,
        double progress,
        string detail)
    {
        statusText.Text = $"● {label}";
        statusText.Foreground = CreateBrush("#2563EB");
        InstallDetailText.Text = detail;

        await Task.Delay(900);

        statusText.Text = $"✓ {label}";
        statusText.Foreground = CreateBrush("#159455");
        InstallProgressBar.Value = progress;
    }

    private void ResetInstallationStatus()
    {
        InstallProgressBar.Value = 0;

        RuntimeInstallStatus.Text = "○ Runtime files";
        ServiceInstallStatus.Text = "○ Windows service";
        ConfigurationInstallStatus.Text =
            "○ Enrollment configuration";
        StartupInstallStatus.Text = "○ Starting Atlas Edge";

        RuntimeInstallStatus.Foreground =
            CreateBrush("#182033");
        ServiceInstallStatus.Foreground =
            CreateBrush("#182033");
        ConfigurationInstallStatus.Foreground =
            CreateBrush("#182033");
        StartupInstallStatus.Foreground =
            CreateBrush("#182033");

        InstallDetailText.Text = "Preparing installation...";
    }

    private void ShowStep(int stepIndex)
    {
        _stepIndex = stepIndex;

        WelcomePanel.Visibility =
            stepIndex == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        ConnectPanel.Visibility =
            stepIndex == 1
                ? Visibility.Visible
                : Visibility.Collapsed;

        InstallPanel.Visibility =
            stepIndex == 2
                ? Visibility.Visible
                : Visibility.Collapsed;

        VerifyPanel.Visibility =
            stepIndex == 3
                ? Visibility.Visible
                : Visibility.Collapsed;

        CompletePanel.Visibility =
            stepIndex == 4
                ? Visibility.Visible
                : Visibility.Collapsed;

        BackButton.IsEnabled = stepIndex > 0;
        NextButton.IsEnabled = true;

        NextButton.Content = stepIndex switch
        {
            0 => "Get Started",
            1 => "Continue",
            2 => "Install",
            3 => "Verify",
            4 => "Finish",
            _ => "Next"
        };

        for (var index = 0; index < _stepLabels.Length; index++)
        {
            var active = index == stepIndex;
            var complete = index < stepIndex;

            _stepLabels[index].Foreground =
                CreateBrush(
                    active
                        ? "#FFFFFF"
                        : complete
                            ? "#90B7FF"
                            : "#92A2BE");

            _stepLabels[index].FontWeight =
                active
                    ? FontWeights.SemiBold
                    : FontWeights.Normal;

            var label = index switch
            {
                0 => "Welcome",
                1 => "Connect to Atlas",
                2 => "Install",
                3 => "Verify",
                4 => "Complete",
                _ => string.Empty
            };

            var symbol = active
                ? "●"
                : complete
                    ? "✓"
                    : "○";

            _stepLabels[index].Text =
                $"{symbol}  {label}";
        }
    }

    private static SolidColorBrush CreateBrush(
        string colorValue)
    {
        var color =
            (Color)ColorConverter.ConvertFromString(
                colorValue);

        return new SolidColorBrush(color);
    }
}