// ============================================================
// SimplePopup  v1.0.0  Build: 20250710
// 简单 WPF 弹窗提示动作，支持自定义文本和标题
// Roslyn v2 零样板模式：禁止 namespace/class
// ============================================================
using System;
using System.Windows;
using Quicker.Public;
public static string Exec(IStepContext context)
{
    try
    {
        // 获取输入变量
        string text = context.GetVarValue("popup_text") as string ?? "你好，来自 Quicker！";
        string title = context.GetVarValue("popup_title") as string ?? "提示";

        // UI 操作必须在 Dispatcher 线程中执行
        Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
        });

        return "OK";
    }
    catch (Exception ex)
    {
        return "ERROR: " + ex.Message;
    }
}
