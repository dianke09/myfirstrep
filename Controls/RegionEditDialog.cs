using System.Windows;
using System.Windows.Controls;

namespace Myfirstrep.Controls;

public sealed class RegionEditDialog : Window
{
    private readonly TextBox _input;

    public string RegionName => _input.Text.Trim();

    public RegionEditDialog(string current)
    {
        Title = "修改归属区域";
        Width = 320;
        Height = 160;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new DockPanel { Margin = new Thickness(12) };
        var label = new TextBlock { Text = "请输入归属区域：", Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(label, Dock.Top);

        _input = new TextBox { Text = current };
        DockPanel.SetDock(_input, Dock.Top);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var ok = new Button { Content = "确定", Width = 70, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { DialogResult = true; Close(); };

        var cancel = new Button { Content = "取消", Width = 70 };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        panel.Children.Add(label);
        panel.Children.Add(_input);
        panel.Children.Add(buttons);

        Content = panel;
    }
}
