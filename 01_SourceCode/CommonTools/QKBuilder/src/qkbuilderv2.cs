using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Quicker.Public;

// ============================================================
// QKBuilder  v2.1.0  2026-07-31
// Quicker 2.1.0 自动构建扳手 — ActionItem2Store API
// 支持 action=build | update | read | publish | copycode | delete
// 内嵌代码，无模板依赖。手工编辑不导出导入，ID 稳定: 0b6ea8ec
// Roslyn v2 零样板模式
// ============================================================

public static string Exec(IStepContext context)
{
    try
    {
        string param = context.GetVarValue("quicker_in_param") as string ?? "";
        if (string.IsNullOrEmpty(param))
        {
            string usage = "QKBuilder v2.1.0\n\n" +
                "build    构建/更新动作\n" +
                "update   更新简介（_简介.md）\n" +
                "read     读取动作信息\n" +
                "publish  发布到共享平台\n" +
                "copycode 修复壳动作模板引用\n" +
                "delete   删除动作\n\n" +
                "build.ps1 -JsonPath <JSON绝对路径>";
            Msg(usage);
            return "OK";
        }

        string action = "", filePath = "";
        foreach (var part in param.Split('&'))
        {
            var kv = part.Split(new[] { '=' }, 2);
            if (kv.Length == 2)
            {
                if (kv[0].ToLower() == "action") action = kv[1];
                if (kv[0].ToLower() == "filepath") filePath = System.Net.WebUtility.UrlDecode(kv[1]);
            }
        }

        var asms = AppDomain.CurrentDomain.GetAssemblies();

        if (action == "build")     return DoBuild(asms, filePath);
        if (action == "update")    return DoUpdate(filePath);
        if (action == "read")      return DoRead(filePath);
        if (action == "publish")   return DoPublish(filePath);
        if (action == "copycode")  return DoCopyCode(asms, filePath);
        if (action == "delete")    return DoDelete(asms, filePath);

        return "ERR:Unknown action: " + action;
    }
    catch (Exception ex)
    {
        string e = ex.GetType().Name + ": " + ex.Message;
        if (ex.InnerException != null) e += " | " + ex.InnerException.Message;
        Msg(e);
        return "ERR:" + ex.Message;
    }
}

// ==================== BUILD ====================

static string DoBuild(Assembly[] asms, string filePath)
{
    if (!File.Exists(filePath)) return "ERR:JSON not found";

    string csPath = Path.ChangeExtension(filePath, ".cs");
    if (!File.Exists(csPath)) return "ERR:CS not found";

    string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
    string csCode = File.ReadAllText(csPath, System.Text.Encoding.UTF8);
    string title = J(json, "Title") ?? "NoTitle";
    string icon = J(json, "Icon") ?? "fa:Solid_Wrench:#4CAF50";

    // ── 类型 ──
    Type st   = T(asms, "Quicker.Domain.Services.ActionItem2Store");
    Type it2  = T(asms, "Quicker.Common.V2.ActionItem2");
    Type pt2  = T(asms, "Quicker.Common.V2.OperationPresentation");
    Type xp   = T(asms, "Quicker.Common.ActionPayloads.XAction.XActionDto");
    Type stepT = T(asms, "Quicker.Common.ActionPayloads.XAction.ActionStepDto");
    Type parT  = T(asms, "Quicker.Common.ActionPayloads.XAction.ActionStepParamDto");
    if (st == null) return "ERR:ActionItem2Store not found";
    if (it2 == null) return "ERR:ActionItem2 not found";

    // ── Store ──
    object store = FindStore(asms, st);
    if (store == null) return "ERR:Store not found";

    // ── Step ──
    object step = Activator.CreateInstance(stepT);
    SP(stepT, step, "StepRunnerKey", "sys:csscript");

    var ip = stepT.GetProperty("InputParams").GetValue(step);
    var addD = ip.GetType().GetMethod("Add");

    object mp = Activator.CreateInstance(parT);
    SP(parT, mp, "Value", "normal_roslyn");
    addD.Invoke(ip, new[] { "mode", mp });

    object sp = Activator.CreateInstance(parT);
    SP(parT, sp, "Value", csCode);
    addD.Invoke(ip, new[] { "script", sp });

    // ── Payload ──
    object payload = Activator.CreateInstance(xp);
    SP(xp, payload, "LimitSingleInstance", true);
    SP(xp, payload, "SummaryExpression", "$$");
    GL(xp, payload, "Steps").Add(step);

    // ── Item ──
    string idStr = J(json, "ActionId");
    bool hasValidId = Guid.TryParse(idStr, out Guid gid)
        && gid != Guid.Empty
        && gid.ToString() != "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
    Guid finalId = hasValidId ? gid : Guid.NewGuid();

    object item = Activator.CreateInstance(it2);
    SP(it2, item, "Id", finalId);
    SP(it2, item, "OperationType", "XAction");
    if (pt2 != null)
    {
        object pres = Activator.CreateInstance(pt2);
        SP(pt2, pres, "Title", title);
        SP(pt2, pres, "Icon", icon);
        SP(it2, item, "Presentation", pres);
    }
    SP(it2, item, "OperationPayload", payload);

    var addM = st.GetMethod("AddOrUpdateAction",
        BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,
        null, new[] { it2 }, null);
    addM.Invoke(store, new[] { item });

    return "OK:" + finalId;
}

// ==================== UPDATE ====================

static string DoUpdate(string filePath)
{
    string dir = Path.GetDirectoryName(filePath);
    string baseName = Path.GetFileNameWithoutExtension(filePath);
    string mdPath = Path.Combine(dir, baseName + "_简介.md");
    if (!File.Exists(mdPath)) mdPath = Path.Combine(dir, baseName + ".md");
    return "OK:update:" + (File.Exists(mdPath) ? mdPath : "NO_MD");
}

// ==================== READ ====================

static string DoRead(string filePath)
{
    if (!File.Exists(filePath)) return "ERR:JSON not found";
    string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
    string id = J(json, "ActionId") ?? "";
    string title = J(json, "Title") ?? "";
    return "OK:read:" + id + "|" + title;
}

// ==================== PUBLISH ====================

static string DoPublish(string filePath)
{
    if (!File.Exists(filePath)) return "ERR:JSON not found";
    string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
    string title = J(json, "Title") ?? "Untitled";
    string sharedId = J(json, "SharedActionId");
    bool isNew = string.IsNullOrEmpty(sharedId) || sharedId == "00000000-0000-0000-0000-000000000000";
    return "OK:publish:" + (isNew ? "NEW" : "UPDATE:" + sharedId) + "|" + title;
}

// ==================== COPYCODE ====================

static string DoCopyCode(Assembly[] asms, string filePath)
{
    if (!File.Exists(filePath)) return "ERR:JSON not found";
    string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
    string shellId = J(json, "ActionId");
    if (string.IsNullOrEmpty(shellId)) return "ERR:No ActionId in JSON";

    Type storeType = T(asms, "Quicker.Domain.Services.ActionItem2Store");
    Type item2Type = T(asms, "Quicker.Common.V2.ActionItem2");
    if (storeType == null || item2Type == null) return "ERR:Type not found";

    object store = FindStore(asms, storeType);
    if (store == null) return "ERR:Store not found";

    var getById = storeType.GetMethod("GetActionById",
        BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
    if (getById == null) return "ERR:GetActionById not found";

    object result = getById.Invoke(store, new object[] { shellId });
    if (result == null) return "ERR:Action not found: " + shellId;

    // 从返回值提取 ActionItem2（可能是 Tuple）
    object shellItem = null;
    Type rt = result.GetType();
    if (rt.Name.Contains("Tuple") || rt.Name.Contains("ValueTuple"))
        shellItem = rt.GetProperty("Item2")?.GetValue(result);
    else if (item2Type.IsAssignableFrom(rt))
        shellItem = result;
    if (shellItem == null) return "ERR:Cannot extract ActionItem2";

    // 修正 SourceActionInfo → 0b6ea8ec
    Type srcT = T(asms, "Quicker.Common.V2.SourceActionInfo");
    if (srcT != null)
    {
        object si = Activator.CreateInstance(srcT);
        SP(srcT, si, "Id", "0b6ea8ec-6e51-47d8-a921-1eb2471f7b51");
        SP(item2Type, shellItem, "SourceActionInfo", si);
    }
    SP(item2Type, shellItem, "UseSource", true);

    var addM = storeType.GetMethod("AddOrUpdateAction",
        BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,
        null, new[] { item2Type }, null);
    addM.Invoke(store, new[] { shellItem });

    return "OK:copycode:" + shellId;
}

// ==================== DELETE ====================

static string DoDelete(Assembly[] asms, string filePath)
{
    if (!File.Exists(filePath)) return "ERR:JSON not found";
    string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
    string targetId = J(json, "ActionId");
    if (string.IsNullOrEmpty(targetId)) return "ERR:No ActionId in JSON";

    Type storeType = T(asms, "Quicker.Domain.Services.ActionItem2Store");
    if (storeType == null) return "ERR:ActionItem2Store not found";
    Type item2Type = T(asms, "Quicker.Common.V2.ActionItem2");
    if (item2Type == null) return "ERR:ActionItem2 not found";

    object store = FindStore(asms, storeType);
    if (store == null) return "ERR:Store not found";

    // 获取 ActionItem2（验证存在）
    var getById = storeType.GetMethod("GetActionById",
        BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
    if (getById == null) return "ERR:GetActionById not found";

    object result = getById.Invoke(store, new object[] { targetId });
    if (result == null) return "ERR:Action not found: " + targetId;

    // 提取 ActionItem2
    object shellItem = null;
    Type rt = result.GetType();
    if (rt.Name.Contains("Tuple") || rt.Name.Contains("ValueTuple"))
        shellItem = rt.GetProperty("Item2")?.GetValue(result);
    else if (item2Type.IsAssignableFrom(rt))
        shellItem = result;
    if (shellItem == null) return "ERR:Cannot extract ActionItem2";

    string title = "?";
    try { title = item2Type.GetProperty("Presentation")?.GetValue(shellItem)?.ToString() ?? "?"; } catch { }

    // 多候选方法名探测删除方法
    string[] candidates = { "DeleteAction", "RemoveAction", "Delete", "Remove" };
    MethodInfo delMethod = null;
    object[] delArgs = null;

    foreach (var name in candidates)
    {
        // 尝试签名为 (Guid id) 或 (string id) 或 (ActionItem2 item)
        foreach (var m in storeType.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance))
        {
            if (!m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            var ps = m.GetParameters();
            if (ps.Length == 1)
            {
                if (ps[0].ParameterType == typeof(Guid))
                    { delMethod = m; delArgs = new object[] { Guid.Parse(targetId) }; break; }
                if (ps[0].ParameterType == typeof(string))
                    { delMethod = m; delArgs = new object[] { targetId }; break; }
                if (item2Type.IsAssignableFrom(ps[0].ParameterType))
                    { delMethod = m; delArgs = new object[] { shellItem }; break; }
            }
        }
        if (delMethod != null) break;
    }

    if (delMethod == null) return "ERR:Delete method not found — tried: " + string.Join(", ", candidates);

    try
    {
        delMethod.Invoke(store, delArgs);
        return "OK:deleted:" + targetId + " | " + title;
    }
    catch (Exception ex)
    {
        return "ERR:Delete failed: " + ex.GetType().Name + ": " + ex.Message;
    }
}

// ==================== Helpers ====================

static Type T(Assembly[] asms, string fn)
{
    foreach (var a in asms) { try { var t = a.GetType(fn); if (t != null) return t; } catch { } }
    return null;
}

static string J(string j, string k)
{
    var m = System.Text.RegularExpressions.Regex.Match(j, "\"" + k + "\"\\s*:\\s*\"([^\"]*)\"");
    return m.Success ? m.Groups[1].Value : null;
}

static object FindStore(Assembly[] asms, Type st)
{
    var mw = Application.Current.MainWindow;
    object store = null;
    if (mw != null) store = R(mw, st, 5);
    if (store != null) return store;
    foreach (var a in asms)
        try { foreach (var t in a.GetTypes()) foreach (var f in t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)) if (f.FieldType==st) { store=f.GetValue(null); if (store!=null) return store; } } catch { }
    return null;
}

static void SP(Type t, object o, string n, object v)
{
    try { var p = t.GetProperty(n, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance); if (p!=null&&p.CanWrite) { p.SetValue(o,v); return; } t.GetField("<"+n+">k__BackingField", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(o,v); } catch { }
}

static IList GL(Type t, object o, string n)
{
    var p = t.GetProperty(n, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
    if (p!=null) return p.GetValue(o) as IList;
    return t.GetField("<"+n+">k__BackingField", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(o) as IList;
}

static object R(object root, Type target, int depth)
{
    if (root==null||depth<0) return null;
    if (target.IsAssignableFrom(root.GetType())) return root;
    foreach (var f in root.GetType().GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance))
        if (target.IsAssignableFrom(f.FieldType)) { var v=f.GetValue(root); if (v!=null) return v; }
    if (depth>0)
        foreach (var f in root.GetType().GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance))
            if (!f.FieldType.IsPrimitive&&f.FieldType!=typeof(string)&&!f.FieldType.IsValueType)
                try { var c=f.GetValue(root); if (c!=null) { var r=R(c,target,depth-1); if (r!=null) return r; } } catch { }
    return null;
}

static void Msg(string m) { Application.Current.Dispatcher.Invoke(() => { MessageBox.Show(m, "QKBuilder"); }); }
