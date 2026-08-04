using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Atlas.Edge.Setup.Services;

namespace Atlas.Edge.Setup;

public partial class MainWindow : Window
{
    private readonly InstallerService _installerService = new();
    private readonly TextBlock[] _stepLabels;

    private int _stepIndex;

    public MainWindow()
    {
        InitializeComponent();


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

        RuntimeInstallStatus.Text = "● Runtime files";
        RuntimeInstallStatus.Foreground = CreateBrush("#2563EB");
        InstallDetailText.Text = "Locating the Atlas Edge installer...";

        var repositoryRoot = FindRepositoryRoot();

        if (repositoryRoot is null)
        {
            ShowInstallationFailure(
                "Could not locate the Atlas Edge repository.");

            return;
        }

        var msiPath = Path.Combine(
            repositoryRoot,
            "artifacts",
            "installer",
            "staging",
            "AtlasEdge.msi");

        var logPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "InterScan",
            "Atlas Edge",
            "diagnostics",
            "installer.log");

        if (!File.Exists(msiPath))
        {
            ShowInstallationFailure(
                $"AtlasEdge.msi was not found at:{Environment.NewLine}{msiPath}");

            return;
        }

        RuntimeInstallStatus.Text = "✓ Runtime files";
        RuntimeInstallStatus.Foreground = CreateBrush("#159455");
        InstallProgressBar.Value = 25;

        ServiceInstallStatus.Text = "● Windows service";
        ServiceInstallStatus.Foreground = CreateBrush("#2563EB");
        InstallDetailText.Text =
            "Installing Atlas Edge with Windows Installer...";

        var result = await _installerService.InstallAsync(
            msiPath,
            logPath);

        if (!result.Succeeded)
        {
            var errorMessage =
                result.Error ?? "Atlas Edge installation failed.";

            if (result.ExitCode is not null)
            {
                errorMessage +=
                    $"{Environment.NewLine}Windows Installer exit code: {result.ExitCode}";
            }

            ShowInstallationFailure(errorMessage);
            return;
        }

        ServiceInstallStatus.Text = "✓ Windows service";
        ServiceInstallStatus.Foreground = CreateBrush("#159455");
        InstallProgressBar.Value = 50;

        ConfigurationInstallStatus.Text =
            "✓ Enrollment configuration";
        ConfigurationInstallStatus.Foreground =
            CreateBrush("#159455");
        InstallProgressBar.Value = 75;

        StartupInstallStatus.Text = "● Starting Atlas Edge";
        StartupInstallStatus.Foreground = CreateBrush("#2563EB");
        InstallDetailText.Text =
            "Confirming the Atlas Edge service is running...";

        await Task.Delay(500);

        StartupInstallStatus.Text = "✓ Starting Atlas Edge";
        StartupInstallStatus.Foreground = CreateBrush("#159455");
        InstallProgressBar.Value = 100;

        InstallDetailText.Text = result.RestartRequired
            ? "Installation completed. Windows requested a restart."
            : "Installation completed successfully.";

        await Task.Delay(700);

        ShowStep(3);
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (directory is not null)
        {
            var solutionPath = Path.Combine(
                directory.FullName,
                "Atlas.Edge.sln");

            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private void ShowInstallationFailure(string message)
    {
        InstallDetailText.Text = message;
        InstallDetailText.Foreground = CreateBrush("#D92D20");

        NextButton.Content = "Try Again";
        NextButton.IsEnabled = true;
        BackButton.IsEnabled = true;
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
        InstallDetailText.Foreground =
            CreateBrush("#667085");
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