using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FaceLocker.Views;

public partial class AdminLockView : UserControl
{
    public AdminLockView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}