using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FaceLocker.Views;

public partial class AdminSettingsView : UserControl
{
    public AdminSettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}