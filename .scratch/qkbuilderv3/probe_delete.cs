//css_ref System.Windows.Forms.dll

using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using Quicker.Public;

// 探测 ActionItem2Store 的所有方法及其参数签名

public static string Exec(IStepContext context)
{
    try
    {
        var asms = AppDomain.CurrentDomain.GetAssemblies();
        Type storeType = null;
        foreach (var a in asms)
        {
            try { storeType = a.GetType("Quicker.Domain.Services.ActionItem2Store"); if (storeType != null) break; }
            catch { }
        }
        if (storeType == null) return "ERR:ActionItem2Store not found";

        var methods = storeType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(m => m.Name);

        var lines = new System.Collections.Generic.List<string>();
        foreach (var m in methods)
        {
            var pars = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            lines.Add(m.ReturnType.Name + " " + m.Name + "(" + pars + ")");
        }

        string msg = string.Join(Environment.NewLine, lines);
        MessageBox.Show(msg, "ActionItem2Store Methods", MessageBoxButton.OK, MessageBoxImage.Information);
        return "OK:" + lines.Count + " methods";
    }
    catch (Exception ex)
    {
        MessageBox.Show("ERR:" + ex.Message, "Probe", MessageBoxButton.OK, MessageBoxImage.Error);
        return "ERR:" + ex.Message;
    }
}