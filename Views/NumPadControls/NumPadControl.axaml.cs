using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace FaceLocker.Views.NumPadControls;

public partial class NumPadControl : UserControl
{
    // 存储原始状态，用于恢复
    private readonly Dictionary<Button, IBrush> _originalBackgrounds = new();

    public NumPadControl()
    {
        InitializeComponent();

        // 加载完成后绑定事件和样式
        this.Loaded += (sender, e) =>
        {
            InitializeButtonEvents();
            BindButtonCommands();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void InitializeButtonEvents()
    {
        try
        {
            // 数字按钮（包括0和小数点）和删除按钮都是白底
            var whiteButtons = new List<Button>
            {
                this.FindControl<Button>("Btn0"),
                this.FindControl<Button>("Btn1"),
                this.FindControl<Button>("Btn2"),
                this.FindControl<Button>("Btn3"),
                this.FindControl<Button>("Btn4"),
                this.FindControl<Button>("Btn5"),
                this.FindControl<Button>("Btn6"),
                this.FindControl<Button>("Btn7"),
                this.FindControl<Button>("Btn8"),
                this.FindControl<Button>("Btn9"),
                this.FindControl<Button>("BtnDot"),
                this.FindControl<Button>("BtnDelete")
            };

            foreach (var button in whiteButtons)
            {
                if (button != null)
                {
                    // 保存原始背景色
                    _originalBackgrounds[button] = button.Background ?? Brushes.White;

                    // 鼠标悬停效果：按钮颜色变深
                    button.PointerEntered += (sender, e) =>
                    {
                        if (button is NumPadButton numPadButton && numPadButton.Key == Key.Back)
                        {
                            // 删除按钮：变深灰色
                            button.Background = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                        }
                        else
                        {
                            // 数字按钮：变深灰色
                            button.Background = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                        }
                    };

                    // 鼠标离开：恢复白色
                    button.PointerExited += (sender, e) =>
                    {
                        button.Background = Brushes.White;
                    };

                    // 鼠标按下：颜色变浅
                    button.PointerPressed += (sender, e) =>
                    {
                        if (button is NumPadButton numPadButton && numPadButton.Key == Key.Back)
                        {
                            // 删除按钮：变浅灰色
                            button.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
                        }
                        else
                        {
                            // 数字按钮：变浅灰色
                            button.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
                        }
                    };

                    // 鼠标释放：恢复悬停或原色
                    button.PointerReleased += (sender, e) =>
                    {
                        // 判断鼠标是否还在按钮上方
                        var position = e.GetPosition(button);
                        var bounds = new Rect(0, 0, button.Bounds.Width, button.Bounds.Height);

                        if (bounds.Contains(position))
                        {
                            // 鼠标还在按钮上方，显示悬停效果
                            if (button is NumPadButton numPadButton && numPadButton.Key == Key.Back)
                            {
                                button.Background = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                            }
                            else
                            {
                                button.Background = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                            }
                        }
                        else
                        {
                            // 鼠标离开按钮，恢复白色
                            button.Background = Brushes.White;
                        }
                    };
                }
            }

            // 确认按钮是蓝底
            var enterButton = this.FindControl<Button>("BtnEnter");
            if (enterButton != null)
            {
                _originalBackgrounds[enterButton] = enterButton.Background ?? Brushes.Blue;

                enterButton.PointerEntered += (sender, e) =>
                {
                    // 悬停：按钮颜色变深（深蓝色）
                    enterButton.Background = new SolidColorBrush(Color.FromRgb(0, 0, 139)); // 深蓝色
                };

                enterButton.PointerExited += (sender, e) =>
                {
                    enterButton.Background = Brushes.Blue;
                };

                enterButton.PointerPressed += (sender, e) =>
                {
                    // 按下：按钮颜色变浅（浅蓝色）
                    enterButton.Background = new SolidColorBrush(Color.FromRgb(135, 206, 250)); // 浅蓝色
                };

                enterButton.PointerReleased += (sender, e) =>
                {
                    var position = e.GetPosition(enterButton);
                    var bounds = new Rect(0, 0, enterButton.Bounds.Width, enterButton.Bounds.Height);

                    if (bounds.Contains(position))
                    {
                        // 鼠标还在按钮上方，显示悬停效果
                        enterButton.Background = new SolidColorBrush(Color.FromRgb(0, 0, 139));
                    }
                    else
                    {
                        enterButton.Background = Brushes.Blue;
                    }
                };
            }
        }
        catch (Exception ex)
        {
            // 避免 Console 噪音：统一由上层日志系统处理（此处静默即可）
        }
    }

    private void BindButtonCommands()
    {
        try
        {
            // 为所有按钮绑定ProcessClick命令
            var allButtons = new List<Button>
            {
                this.FindControl<Button>("Btn0"),
                this.FindControl<Button>("Btn1"),
                this.FindControl<Button>("Btn2"),
                this.FindControl<Button>("Btn3"),
                this.FindControl<Button>("Btn4"),
                this.FindControl<Button>("Btn5"),
                this.FindControl<Button>("Btn6"),
                this.FindControl<Button>("Btn7"),
                this.FindControl<Button>("Btn8"),
                this.FindControl<Button>("Btn9"),
                this.FindControl<Button>("BtnDot"),
                this.FindControl<Button>("BtnDelete"),
                this.FindControl<Button>("BtnEnter")
            };

            foreach (var button in allButtons)
            {
                if (button is NumPadButton numPadButton)
                {
                    // 使用Click事件
                    numPadButton.Click += (sender, e) =>
                    {
                        ProcessClick(numPadButton.Key);
                    };
                }
            }
        }
        catch (Exception ex)
        {
            // 避免 Console 噪音：统一由上层日志系统处理（此处静默即可）
        }
    }

    // 定义输入值属性，用于展示已有的数据
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<NumPadControl, string>(nameof(Text), string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    // 是否Enter键触发的路由事件
    public static readonly RoutedEvent<RoutedEventArgs> EnterPressedEvent =
        RoutedEvent.Register<NumPadControl, RoutedEventArgs>(nameof(EnterPressed), RoutingStrategies.Direct);

    public event EventHandler<RoutedEventArgs> EnterPressed
    {
        add => AddHandler(EnterPressedEvent, value);
        remove => RemoveHandler(EnterPressedEvent, value);
    }

    // mvvm方式的Enter键触发
    public static readonly StyledProperty<ICommand?> EnterCommandProperty =
        AvaloniaProperty.Register<NumPadControl, ICommand?>(nameof(EnterCommand));

    public ICommand? EnterCommand
    {
        get => GetValue(EnterCommandProperty);
        set => SetValue(EnterCommandProperty, value);
    }

    /// <summary>
    /// 字典
    /// </summary>
    private static readonly Dictionary<Key, string> KeyInputMapping = new()
    {
        [Key.NumPad0] = "0",
        [Key.NumPad1] = "1",
        [Key.NumPad2] = "2",
        [Key.NumPad3] = "3",
        [Key.NumPad4] = "4",
        [Key.NumPad5] = "5",
        [Key.NumPad6] = "6",
        [Key.NumPad7] = "7",
        [Key.NumPad8] = "8",
        [Key.NumPad9] = "9",
    };

    /// <summary>
    /// 按键
    /// </summary>
    /// <param name="key"></param>
    public void ProcessClick(Key key)
    {
        if (KeyInputMapping.TryGetValue(key, out var s))
        {
            Text += s;
        }
        else if (Key.Decimal == key)
        {
            // 不能以点开头
            if (Text == string.Empty)
            {
                return;
            }

            // 不能有多个点
            if (Text.Contains('.'))
            {
                return;
            }

            Text += '.';
        }
        else if (Key.Back == key)
        {
            if (!string.IsNullOrEmpty(Text))
            {
                Text = Text[..^1];
            }
        }
        else if (Key.Enter == key)
        {
            // 如果以.结尾，则去掉
            if (Text.EndsWith('.'))
            {
                Text = Text[..^1];
            }

            // 执行command
            if (EnterCommand?.CanExecute(null) == true)
            {
                EnterCommand.Execute(null);
            }

            // 触发事件
            var args = new RoutedEventArgs(EnterPressedEvent);
            RaiseEvent(args);
        }
    }
}
