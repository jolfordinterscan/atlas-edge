using System.Windows.Controls;

namespace Atlas.Edge.Setup.Views;

public partial class ConnectView : UserControl
{
    public ConnectView()
    {
        InitializeComponent();

        ComputerNameText.Text = Environment.MachineName;
    }

    public string AtlasServer =>
        AtlasServerTextBox.Text.Trim();

    public string EnrollmentCode =>
        EnrollmentCodeBox.Password.Trim();
}
