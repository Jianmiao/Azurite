using Azurite;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length == 2)
{
    var ok = HostInteropContract.TryVerify(args[0], args[1], out var message);
    Console.WriteLine((ok ? "PASS " : "FAIL ") + message);
    return ok ? 0 : 1;
}
var tests = new (string, Action<Fixture>, bool)[]
{
    ("valid referenced structure", _ => { }, true),
    ("regenerated MVID and unrelated extra members", f => { f.Host.MainModule.Mvid = Guid.NewGuid(); f.Type.Fields.Add(new FieldDefinition("Extra", FieldAttributes.Public, f.Host.MainModule.TypeSystem.String)); }, true),
    ("missing referenced method", f => f.Type.Methods.Remove(f.Method), false),
    ("changed return type", f => f.Method.ReturnType = f.Host.MainModule.TypeSystem.String, false),
    ("changed parameter type", f => f.Method.Parameters[0].ParameterType = f.Host.MainModule.TypeSystem.String, false),
    ("changed method static/instance", f => { f.Method.IsStatic = true; f.Method.HasThis = false; }, false),
    ("changed method generic arity", f => f.Method.GenericParameters.Add(new GenericParameter("T", f.Method)), false),
    ("changed field static/instance", f => f.Field.IsStatic = true, false),
    ("changed field type", f => f.Field.FieldType = f.Host.MainModule.TypeSystem.String, false),
    ("changed required UIPanel base", f => f.Type.BaseType = f.Host.MainModule.TypeSystem.Object, false),
    ("required host class cannot become an interface", f => f.Type.IsInterface = true, false),
    ("referenced value type cannot become a class", f => f.Value.BaseType = f.Host.MainModule.TypeSystem.Object, false),
    ("referenced generic type arity cannot change", f => f.Generic.GenericParameters.Add(new GenericParameter("U", f.Generic)), false),
    ("required delegate Invoke signature cannot change", f => f.DelegateInvoke.Parameters.Add(new ParameterDefinition(f.Host.MainModule.TypeSystem.Int32)), false),
    ("duplicate method signature", f => { var m = new MethodDefinition("Read", MethodAttributes.Public, f.Host.MainModule.TypeSystem.Int32); m.Parameters.Add(new ParameterDefinition(f.Host.MainModule.TypeSystem.Int32)); m.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); f.Type.Methods.Add(m); }, false),
    ("missing host assembly", f => f.DeleteHost = true, false),
    ("missing settings widget refresh method", f => f.SettingPanel.Methods.Remove(f.UpdateWidgets), false),
    ("FPS tier change callback cannot become static", f => f.FpsChanged.IsStatic = true, false),
    ("FPS slider callback cannot be missing", f => f.SettingPanel.Methods.Remove(f.FpsChanged), false),
    ("FPS label accessor cannot change widget type", f => f.Fps30Label.ReturnType = f.Host.MainModule.TypeSystem.String, false),
    ("setting labels cannot lose a generated getter", f => f.SettingWidgets.Methods.Remove(f.Fps30Label), false)
};
var failures = 0;
foreach (var (name, mutate, expected) in tests)
{
    using var f = new Fixture();
    mutate(f); f.Save();
    var actual = HostInteropContract.TryVerify(f.PluginPath, f.Root, out var reason);
    if (actual == expected) Console.WriteLine("PASS " + name);
    else { failures++; Console.WriteLine("FAIL " + name + ": " + reason); }
}
Console.WriteLine($"RESULT {tests.Length - failures}/{tests.Length}; Cecil metadata only, no AA/native code execution.");
using (var signatureModule = ModuleDefinition.CreateModule("label-signature-fixture", ModuleKind.Dll))
{
    var validCallback = new MethodDefinition("UpdateWidgets", MethodAttributes.Public, signatureModule.TypeSystem.Void);
    var staticCallback = new MethodDefinition("UpdateWidgets", MethodAttributes.Public | MethodAttributes.Static, signatureModule.TypeSystem.Void);
    var argumentCallback = new MethodDefinition("UpdateWidgets", MethodAttributes.Public, signatureModule.TypeSystem.Void);
    argumentCallback.Parameters.Add(new ParameterDefinition(signatureModule.TypeSystem.Int32));
    var validGetter = new MethodDefinition("get_fps30Label", MethodAttributes.Public, new TypeReference("", "UILabel", signatureModule, signatureModule));
    var wrongGetter = new MethodDefinition("get_fps30Label", MethodAttributes.Public | MethodAttributes.Static, signatureModule.TypeSystem.String);
    foreach (var (name, actual, expected) in new[]
    {
        ("valid frame-rate callback shape", HostInteropContract.IsFrameRateLabelCallback(validCallback), true),
        ("reject static frame-rate callback", HostInteropContract.IsFrameRateLabelCallback(staticCallback), false),
        ("reject callback signature drift", HostInteropContract.IsFrameRateLabelCallback(argumentCallback), false),
        ("valid frame-rate widget getter", HostInteropContract.IsFrameRateLabelGetter(validGetter), true),
        ("reject static or wrong-type frame-rate getter", HostInteropContract.IsFrameRateLabelGetter(wrongGetter), false)
    })
    {
        if (actual == expected) Console.WriteLine("PASS " + name);
        else { failures++; Console.WriteLine("FAIL " + name); }
    }
}
return failures == 0 ? 0 : 1;

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "Azurite-InteropTests-" + Guid.NewGuid().ToString("N"));
    public string PluginPath => Path.Combine(Root, "mod", "Azurite.Fixture.dll");
    public AssemblyDefinition Host { get; }
    public AssemblyDefinition Plugin { get; }
    public TypeDefinition Type { get; }
    public MethodDefinition Method { get; }
    public FieldDefinition Field { get; }
    public TypeDefinition Value { get; }
    public TypeDefinition Generic { get; }
    public MethodDefinition DelegateInvoke { get; }
    public TypeDefinition SettingPanel { get; }
    public TypeDefinition SettingWidgets { get; }
    public MethodDefinition UpdateWidgets { get; }
    public MethodDefinition FpsChanged { get; }
    public MethodDefinition Fps30Label { get; }
    public bool DeleteHost;
    public Fixture()
    {
        Directory.CreateDirectory(Path.Combine(Root, "BepInEx", "interop"));
        Directory.CreateDirectory(Path.Combine(Root, "mod"));
        Host = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Assembly-CSharp", new Version(0, 0)), "Assembly-CSharp", ModuleKind.Dll);
        var baseType = new TypeDefinition("", "UIRect", TypeAttributes.Public | TypeAttributes.Class, Host.MainModule.TypeSystem.Object);
        Host.MainModule.Types.Add(baseType);
        Type = new TypeDefinition("", "UIPanel", TypeAttributes.Public | TypeAttributes.Class, baseType);
        Host.MainModule.Types.Add(Type);
        Method = new MethodDefinition("Read", MethodAttributes.Public, Host.MainModule.TypeSystem.Int32);
        Method.Parameters.Add(new ParameterDefinition(Host.MainModule.TypeSystem.Int32));
        Method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0)); Method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        Type.Methods.Add(Method);
        Field = new FieldDefinition("Count", FieldAttributes.Public, Host.MainModule.TypeSystem.Int32); Type.Fields.Add(Field);
        Value = new TypeDefinition("", "Point", TypeAttributes.Public | TypeAttributes.Sealed, Host.MainModule.ImportReference(typeof(ValueType)));
        Host.MainModule.Types.Add(Value);
        var pointField = new FieldDefinition("Point", FieldAttributes.Public, Value); Type.Fields.Add(pointField);
        Generic = new TypeDefinition("", "Holder`1", TypeAttributes.Public, Host.MainModule.TypeSystem.Object);
        Generic.GenericParameters.Add(new GenericParameter("T", Generic)); Host.MainModule.Types.Add(Generic);
        var holder = new GenericInstanceType(Generic); holder.GenericArguments.Add(Host.MainModule.TypeSystem.Int32);
        var genericField = new FieldDefinition("Holder", FieldAttributes.Public, holder); Type.Fields.Add(genericField);
        var multicast = new TypeDefinition("Il2CppSystem", "MulticastDelegate", TypeAttributes.Public, Host.MainModule.TypeSystem.Object);
        Host.MainModule.Types.Add(multicast);
        var geometry = new TypeDefinition("", "OnGeometryUpdated", TypeAttributes.NestedPublic | TypeAttributes.Sealed, multicast);
        Type.NestedTypes.Add(geometry);
        DelegateInvoke = new MethodDefinition("Invoke", MethodAttributes.Public, Host.MainModule.TypeSystem.Void);
        DelegateInvoke.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); geometry.Methods.Add(DelegateInvoke);
        var delegateField = new FieldDefinition("Geometry", FieldAttributes.Public, geometry); Type.Fields.Add(delegateField);
        SettingPanel = new TypeDefinition("", "SettingPanel", TypeAttributes.Public | TypeAttributes.Class, Host.MainModule.TypeSystem.Object);
        Host.MainModule.Types.Add(SettingPanel);
        UpdateWidgets = AddMethod(SettingPanel, "UpdateWidgets", Host.MainModule.TypeSystem.Void);
        FpsChanged = AddMethod(SettingPanel, "OnFpsSliderChanged", Host.MainModule.TypeSystem.Void);
        SettingWidgets = new TypeDefinition("SettingPanel", "SettingWidgets", TypeAttributes.NestedPublic | TypeAttributes.Class, Host.MainModule.TypeSystem.Object);
        SettingPanel.NestedTypes.Add(SettingWidgets);
        AddMethod(SettingPanel, "get_widgets", SettingWidgets);
        var label = new TypeDefinition("", "UILabel", TypeAttributes.Public | TypeAttributes.Class, Host.MainModule.TypeSystem.Object);
        Host.MainModule.Types.Add(label);
        Fps30Label = AddMethod(SettingWidgets, "get_fps30Label", label);
        AddMethod(SettingWidgets, "get_fps60Label", label);
        AddMethod(SettingWidgets, "get_fpsInfLabel", label);
        Plugin = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Azurite.Fixture", new Version(1, 0)), "Azurite.Fixture", ModuleKind.Dll);
        var main = new TypeDefinition("", "Consumer", TypeAttributes.Public | TypeAttributes.Class, Plugin.MainModule.TypeSystem.Object); Plugin.MainModule.Types.Add(main);
        var run = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, Plugin.MainModule.TypeSystem.Int32); main.Methods.Add(run);
        run.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull)); run.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        run.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, Plugin.MainModule.ImportReference(Method)));
        run.Body.Instructions.Add(Instruction.Create(OpCodes.Pop)); run.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        run.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, Plugin.MainModule.ImportReference(Field))); run.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        foreach (var extraField in new[] { pointField, genericField, delegateField })
        {
            run.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
            run.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, Plugin.MainModule.ImportReference(extraField)));
            run.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        }
        run.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0)); run.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }
    public void Save()
    {
        Plugin.Write(PluginPath);
        if (!DeleteHost) Host.Write(Path.Combine(Root, "BepInEx", "interop", "Assembly-CSharp.dll"));
    }
    private static MethodDefinition AddMethod(TypeDefinition type, string name, TypeReference result)
    {
        var method = new MethodDefinition(name, MethodAttributes.Public, result);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        return method;
    }
    public void Dispose()
    {
        Host.Dispose(); Plugin.Dispose();
        // Only this exact test-created GUID directory is removed.
        var resolved = Path.GetFullPath(Root);
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("Azurite-InteropTests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refuse cleanup outside the test-created directory.");
        Directory.Delete(resolved, true);
    }
}
