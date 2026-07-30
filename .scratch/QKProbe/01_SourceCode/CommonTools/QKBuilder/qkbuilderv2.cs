using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Quicker.Public;

public static string Exec(IStepContext context)
{
    string diag = "=== QK扳手 诊断 ===\n";
    var asms = AppDomain.CurrentDomain.GetAssemblies();

    // ── Step 1: 找类型 ──
    Type st  = F(asms, "Quicker.Domain.Services.ActionItem2Store");
    Type it2 = F(asms, "Quicker.Common.V2.ActionItem2");
    Type pt2 = F(asms, "Quicker.Common.V2.OperationPresentation");
    Type xp  = F(asms, "Quicker.Common.ActionPayloads.XAction.XActionDto");
    Type stepT = F(asms, "Quicker.Common.ActionPayloads.XAction.ActionStepDto");
    Type parT  = F(asms, "Quicker.Common.ActionPayloads.XAction.ActionStepParamDto");

    diag += "1. Store=" + S(st) + "\n";
    diag += "2. Item2=" + S(it2) + "\n";
    diag += "3. Pres=" + S(pt2) + "\n";
    diag += "4. XActionDto=" + S(xp) + "\n";
    diag += "5. StepDto=" + S(stepT) + "\n";
    diag += "6. ParamDto=" + S(parT) + "\n";

    if (st == null) { Show(diag + "\n❌ Store 找不到！"); return "DIAG"; }

    // ── Step 2: 找 Store 实例 (MainWindow递归) ──
    var mw = Application.Current.MainWindow;
    object store = null;
    if (mw != null) { store = R(mw, st, 5); diag += "7. MW→Store=" + S(store) + "\n"; }
    else { diag += "7. MainWindow=null\n"; }

    // ── Step 3: 找 Store 实例 (静态字段) ──
    if (store == null)
    {
        foreach (var a in asms)
        {
            try
            {
                foreach (var t in a.GetTypes())
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                        if (f.FieldType == st) { store = f.GetValue(null); if (store != null) break; }
            }
            catch { }
        }
        diag += "8. Static→Store=" + S(store) + "\n";
    }

    if (store == null) { Show(diag + "\n❌ Store 实例找不到！"); return "DIAG"; }

    // ── Step 4: 查 AddOrUpdateAction 方法 ──
    var addM = st.GetMethod("AddOrUpdateAction",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
        null, new[] { it2 }, null);
    diag += "9. AddOrUpdate(ActionItem2)=" + S(addM) + "\n";

    if (addM == null)
    {
        // 列出所有 public 方法
        foreach (var m in st.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (m.Name.Contains(".")) continue;
            var ps = string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name));
            diag += "   M: " + m.Name + "(" + ps + ")\n";
        }
    }

    // ── Step 5: 测试创建 ActionItem2 ──
    try
    {
        // StepDto
        object s1 = Activator.CreateInstance(stepT);
        SP(stepT, s1, "StepRunnerKey", "sys:csscript");
        SP(stepT, s1, "Note", "");

        // InputParams
        var ip = stepT.GetProperty("InputParams").GetValue(s1);
        var add = ip.GetType().GetMethod("Add");

        object pm = Activator.CreateInstance(parT);
        SP(parT, pm, "Value", "normal_roslyn");
        add.Invoke(ip, new[] { "mode", pm });

        object ps2 = Activator.CreateInstance(parT);
        SP(parT, ps2, "Value", "return \"OK\";");
        add.Invoke(ip, new[] { "script", ps2 });

        // XActionDto
        object payload = Activator.CreateInstance(xp);
        SP(xp, payload, "LimitSingleInstance", true);
        SP(xp, payload, "SummaryExpression", "$$");
        var steps = GL(xp, payload, "Steps");
        steps.Add(s1);

        // ActionItem2
        object item = Activator.CreateInstance(it2);
        SP(it2, item, "Id", Guid.NewGuid());
        SP(it2, item, "OperationType", "XAction");
        if (pt2 != null)
        {
            object pr = Activator.CreateInstance(pt2);
            SP(pt2, pr, "Title", "DIAG_TEST");
            SP(pt2, pr, "Icon", "fa:Solid_Wrench:#4CAF50");
            SP(it2, item, "Presentation", pr);
        }
        SP(it2, item, "OperationPayload", payload);

        diag += "10. 创建 ActionItem2: ✅\n";

        // ── Step 6: 调用 AddOrUpdateAction ──
        if (addM != null)
        {
            addM.Invoke(store, new[] { item });
            diag += "11. AddOrUpdateAction: ✅ 成功！";
        }
    }
    catch (Exception ex)
    {
        diag += "❌ " + ex.GetType().Name + ": " + ex.Message + "\n";
        if (ex.InnerException != null) diag += "  Inner: " + ex.InnerException.Message + "\n";
    }

    Show(diag);
    return "DIAG";
}

static string S(object o) => o != null ? "✅" : "❌ null";
static Type F(Assembly[] asms, string fn) { foreach (var a in asms) { try { var t = a.GetType(fn); if (t != null) return t; } catch { } } return null; }
static string J(string j, string k) { var m = System.Text.RegularExpressions.Regex.Match(j, "\"" + k + "\"\\s*:\\s*\"([^\"]*)\""); return m.Success ? m.Groups[1].Value : null; }

static void SP(Type t, object o, string n, object v)
{
    try
    {
        var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p != null && p.CanWrite) { p.SetValue(o, v); return; }
        t.GetField("<" + n + ">k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(o, v);
    }
    catch { }
}

static IList GL(Type t, object o, string n)
{
    var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    if (p != null) return p.GetValue(o) as IList;
    return t.GetField("<" + n + ">k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o) as IList;
}

static object R(object root, Type target, int depth)
{
    if (root == null || depth < 0) return null;
    if (target.IsAssignableFrom(root.GetType())) return root;
    foreach (var f in root.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        if (target.IsAssignableFrom(f.FieldType)) { var v = f.GetValue(root); if (v != null) return v; }
    if (depth > 0)
        foreach (var f in root.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (!f.FieldType.IsPrimitive && f.FieldType != typeof(string) && !f.FieldType.IsValueType)
                try { var c = f.GetValue(root); if (c != null) { var r = R(c, target, depth - 1); if (r != null) return r; } } catch { }
    return null;
}

static void Show(string m) { Application.Current.Dispatcher.Invoke(() => { System.Windows.MessageBox.Show(m, "QK扳手 诊断"); }); }
