using System.Runtime.InteropServices;
using System.Windows.Controls;

namespace Atlas.Edge.Setup.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();

        ComputerNameText.Text = Environment.MachineName;
        OperatingSystemText.Text =
            $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
    }
}
