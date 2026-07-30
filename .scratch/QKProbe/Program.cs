using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

string dllPath = @"C:\Program Files\Quicker\Quicker.dll";
string filter = args.Length > 0 ? args[0] : "Template";

using var fs = File.OpenRead(dllPath);
using var pe = new PEReader(fs);
var mr = pe.GetMetadataReader();

foreach (var tdh in mr.TypeDefinitions)
{
    var td = mr.GetTypeDefinition(tdh);
    var name = mr.GetString(td.Name);
    var ns = mr.GetString(td.Namespace);
    var fn = string.IsNullOrEmpty(ns) ? name : ns + "." + name;

    if (!fn.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

    Console.WriteLine($"\n--- {fn} ---");
    foreach (var ph in td.GetProperties())
    {
        var p = mr.GetPropertyDefinition(ph);
        Console.WriteLine("  Prop: " + mr.GetString(p.Name));
    }
    foreach (var mh in td.GetMethods())
    {
        var m = mr.GetMethodDefinition(mh);
        var mn = mr.GetString(m.Name);
        if (mn.Contains("get_")||mn.Contains("set_")||mn.Contains(".")) continue;
        if (m.Attributes.HasFlag(MethodAttributes.Public))
            Console.WriteLine("  Method: " + mn);
    }
}
