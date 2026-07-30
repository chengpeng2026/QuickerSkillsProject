using System;
using System.Windows;
using Quicker.Public;

// HelloPopup — 弹窗显示你好

public static string Exec(IStepContext context)
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        MessageBox.Show("你好", "HelloPopup", MessageBoxButton.OK, MessageBoxImage.Information);
    });

    return "OK";
}
