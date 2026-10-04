using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Azurite;

/// <summary>
/// Checks the APIs actually referenced by this plugin against the installed
/// generated bindings. Generated MVIDs, PE timestamps and byte hashes are not
/// compatibility signals. The caller must separately verify the native binary
/// and metadata identity; this check cannot establish native runtime semantics.
/// No inspected assembly is loaded for execution.
/// </summary>
internal static class HostInteropContract
{
    private static readonly Dictionary<string, string> RequiredAncestors = new(StringComparer.Ordinal)
    {
        ["Studio.Scripts.ScriptNodeInspector"] = "UnityEngine.MonoBehaviour",
        ["Studio.Scripts.StudioCommon"] = "UnityEngine.MonoBehaviour",
        ["Studio.Scripts.CenterableUIScrollView"] = "UIScrollView",
        ["SettingPanel"] = "UnityEngine.MonoBehaviour",
        ["UserSettings"] = "Singleton`1<UserSettings>",
        ["UIScrollView"] = "UnityEngine.MonoBehaviour",
        ["UIPanel"] = "UIRect",
        ["UITexture"] = "UIWidget",
        ["UIWidget"] = "UIRect",
        ["UIRect"] = "UnityEngine.MonoBehaviour",
        ["UnityEngine.MonoBehaviour"] = "UnityEngine.Behaviour",
        ["UnityEngine.Object"] = "Il2CppSystem.Object",
        ["Il2CppSystem.Object"] = "Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase",
        ["UICamera/FloatDelegate"] = "Il2CppSystem.MulticastDelegate",
        ["UIPanel/OnGeometryUpdated"] = "Il2CppSystem.MulticastDelegate",
        ["UIPanel/OnClippingMoved"] = "Il2CppSystem.MulticastDelegate"
    };

    internal static bool TryVerify(string pluginPath, string gameRoot, out string reason)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pluginPath) || string.IsNullOrWhiteSpace(gameRoot))
                throw new InvalidDataException("Plugin or host path is missing.");
            pluginPath = Path.GetFullPath(pluginPath);
            gameRoot = Path.GetFullPath(gameRoot);
            if (!File.Exists(pluginPath)) throw new FileNotFoundException("Plugin assembly is missing.");
            using var resolver = new ContractResolver(Path.GetDirectoryName(pluginPath)!, gameRoot);
            using var plugin = AssemblyDefinition.ReadAssembly(pluginPath, new ReaderParameters
                { AssemblyResolver = resolver, ReadingMode = ReadingMode.Deferred, ReadSymbols = false });
            var module = plugin.MainModule;
            var typeCount = 0;
            var memberCount = 0;
            var validatedTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in module.GetTypeReferences())
            {
                ValidateType(reference, validatedTypes);
                typeCount++;
            }
            foreach (var reference in module.GetMemberReferences())
            {
                if (reference is MethodReference method) ValidateMethod(method);
                else if (reference is FieldReference field) ValidateField(field);
                memberCount++;
            }
            // A FieldRef does not encode static/instance. Its actual opcode does.
            foreach (var type in AllTypes(module.Types))
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not FieldReference field || field.DeclaringType.Scope == module) continue;
                    bool? staticUse = instruction.OpCode.Code switch
                    {
                        Code.Ldsfld or Code.Stsfld or Code.Ldsflda => true,
                        Code.Ldfld or Code.Stfld or Code.Ldflda => false,
                        _ => null
                    };
                    if (staticUse.HasValue && Resolve(field).IsStatic != staticUse.Value)
                        throw new InvalidDataException("Field static/instance contract changed: " + field.FullName);
                }
            }
            reason = $"generated binding contract verified: {typeCount} type references, {memberCount} member references; static inspection only";
            return true;
        }
        catch (Exception error)
        {
            reason = "generated binding contract rejected: " + error.GetType().Name + "; " + error.Message;
            return false;
        }
    }

    private static void ValidateType(TypeReference reference, HashSet<string> seen)
    {
        if (reference is GenericParameter) return;
        if (reference is TypeSpecification specification)
        {
            ValidateType(specification.ElementType, seen);
            if (reference is GenericInstanceType generic)
            {
                var definition = generic.ElementType.Resolve() ?? throw new TypeLoadException(reference.FullName);
                if (definition.GenericParameters.Count != generic.GenericArguments.Count)
                    throw new InvalidDataException("Generic type arity changed: " + reference.FullName);
                foreach (var argument in generic.GenericArguments) ValidateType(argument, seen);
            }
            return;
        }
        var key = reference.Scope?.Name + ":" + reference.FullName;
        if (!seen.Add(key)) return;
        var resolved = reference.Resolve() ?? throw new TypeLoadException(reference.FullName);
        // TypeRef metadata alone has no class/value flag. Cecil can leave a
        // reference used only by a custom attribute at the default false;
        // positive value-type signatures and required class ancestors below
        // are authoritative, absence of that flag is not.
        if (resolved.FullName != reference.FullName || (reference.IsValueType && !resolved.IsValueType))
            throw new InvalidDataException("Type class/value representation changed: " + reference.FullName);
        if (ReferenceArity(reference) != resolved.GenericParameters.Count)
            throw new InvalidDataException("Generic type definition arity changed: " + reference.FullName);
        if (RequiredAncestors.TryGetValue(reference.FullName, out var expected) && (resolved.IsValueType || resolved.IsInterface || !HasAncestor(resolved, expected)))
            throw new InvalidDataException("Required host base/delegate relationship changed: " + reference.FullName + " -> " + expected);
        if (expected == "Il2CppSystem.MulticastDelegate") ValidateDelegate(resolved);
    }

    private static void ValidateDelegate(TypeDefinition definition)
    {
        var parameterNames = definition.FullName switch
        {
            "UICamera/FloatDelegate" => new[] { "UnityEngine.GameObject", "System.Single" },
            "UIPanel/OnGeometryUpdated" => Array.Empty<string>(),
            "UIPanel/OnClippingMoved" => new[] { "UIPanel" },
            _ => throw new InvalidDataException("Unexpected required delegate: " + definition.FullName)
        };
        var invoke = definition.Methods.Where(m => m.Name == "Invoke").ToArray();
        if (invoke.Length != 1 || invoke[0].IsStatic || invoke[0].ReturnType.FullName != "System.Void" ||
            !invoke[0].Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameterNames))
            throw new InvalidDataException("Delegate Invoke contract changed: " + definition.FullName);
    }

    private static bool HasAncestor(TypeDefinition type, string expected)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parent = type.BaseType;
        for (var depth = 0; parent != null && depth < 64; depth++)
        {
            if (parent.FullName == expected) return true;
            if (!seen.Add(parent.FullName)) return false;
            parent = (parent.Resolve() ?? throw new TypeLoadException(parent.FullName)).BaseType;
        }
        return false;
    }

    private static int ReferenceArity(TypeReference reference)
    {
        if (reference.HasGenericParameters) return reference.GenericParameters.Count;
        // Non-instantiated TypeRef rows do not carry GenericParam rows. CLR
        // generic arity is encoded in each declaring type's metadata name.
        var count = 0;
        for (var current = reference; current != null; current = current.DeclaringType)
        {
            var marker = current.Name.LastIndexOf('`');
            if (marker >= 0 && int.TryParse(current.Name.Substring(marker + 1), out var arity)) count += arity;
        }
        return count;
    }

    private static void ValidateMethod(MethodReference reference)
    {
        var element = reference is MethodSpecification specification ? specification.ElementMethod : reference;
        var resolved = element.Resolve() ?? throw new MissingMethodException(element.FullName);
        ValidateSignatureType(element.ReturnType);
        foreach (var parameter in element.Parameters) ValidateSignatureType(parameter.ParameterType);
        if (element.Name != resolved.Name || element.HasThis == resolved.IsStatic ||
            element.ExplicitThis != resolved.ExplicitThis || element.GenericParameters.Count != resolved.GenericParameters.Count ||
            element.Parameters.Count != resolved.Parameters.Count || !SameType(element.ReturnType, resolved.ReturnType))
            throw new InvalidDataException("Method contract changed: " + element.FullName);
        for (var index = 0; index < element.Parameters.Count; index++)
            if (!SameType(element.Parameters[index].ParameterType, resolved.Parameters[index].ParameterType))
                throw new InvalidDataException("Method parameter contract changed: " + element.FullName);
        if (reference is GenericInstanceMethod generic && generic.GenericArguments.Count != resolved.GenericParameters.Count)
            throw new InvalidDataException("Generic method arity changed: " + element.FullName);
        // Reject duplicate signatures rather than letting metadata order select
        // an arbitrary implementation. Resolve may legitimately find a base.
        var matches = resolved.DeclaringType.Methods.Count(candidate => SameMethod(candidate, resolved));
        if (matches != 1) throw new InvalidDataException("Ambiguous method contract: " + element.FullName);
    }

    private static bool SameMethod(MethodDefinition left, MethodDefinition right) => left.Name == right.Name &&
        left.IsStatic == right.IsStatic && left.GenericParameters.Count == right.GenericParameters.Count &&
        left.Parameters.Count == right.Parameters.Count && SameType(left.ReturnType, right.ReturnType) &&
        left.Parameters.Zip(right.Parameters, (a, b) => SameType(a.ParameterType, b.ParameterType)).All(value => value);

    private static FieldDefinition Resolve(FieldReference reference) => reference.Resolve() ?? throw new MissingFieldException(reference.FullName);

    private static void ValidateField(FieldReference reference)
    {
        var resolved = Resolve(reference);
        ValidateSignatureType(reference.FieldType);
        if (!SameType(reference.FieldType, resolved.FieldType) ||
            resolved.DeclaringType.Fields.Count(f => f.Name == resolved.Name && SameType(f.FieldType, resolved.FieldType)) != 1)
            throw new InvalidDataException("Field type/uniqueness contract changed: " + reference.FullName);
    }

    private static void ValidateSignatureType(TypeReference type)
    {
        if (type is GenericParameter || type.MetadataType == MetadataType.Void) return;
        if (type is TypeSpecification specification)
        {
            ValidateSignatureType(specification.ElementType);
            if (type is GenericInstanceType generic)
                foreach (var argument in generic.GenericArguments) ValidateSignatureType(argument);
            return;
        }
        var resolved = type.Resolve() ?? throw new TypeLoadException(type.FullName);
        if (type.IsValueType != resolved.IsValueType)
            throw new InvalidDataException("Signature class/value representation changed: " + type.FullName);
    }

    private static bool SameType(TypeReference left, TypeReference right) => Shape(left) == Shape(right);
    private static string Shape(TypeReference type) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        GenericInstanceType generic => Shape(generic.ElementType) + "<" + string.Join(",", generic.GenericArguments.Select(Shape)) + ">",
        ArrayType array => Shape(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]",
        ByReferenceType byReference => Shape(byReference.ElementType) + "&",
        PointerType pointer => Shape(pointer.ElementType) + "*",
        RequiredModifierType modifier => "modreq(" + Shape(modifier.ModifierType) + ")" + Shape(modifier.ElementType),
        OptionalModifierType modifier => "modopt(" + Shape(modifier.ModifierType) + ")" + Shape(modifier.ElementType),
        _ => type.FullName
    };

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
        }
    }

    private sealed class ContractResolver : IAssemblyResolver
    {
        private readonly string _own, _interop, _core, _runtime;
        private readonly Dictionary<string, AssemblyDefinition> _loaded = new(StringComparer.OrdinalIgnoreCase);
        public ContractResolver(string own, string root)
        {
            _own = own;
            _interop = Path.Combine(root, "BepInEx", "interop");
            _core = Path.Combine(root, "BepInEx", "core");
            _runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        }
        public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());
        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (_loaded.TryGetValue(name.Name, out var cached)) return cached;
            if (string.IsNullOrWhiteSpace(name.Name) || Path.GetFileName(name.Name) != name.Name || name.Name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new AssemblyResolutionException(name);
            var runtime = name.Name is "mscorlib" or "netstandard" or "System" || name.Name.StartsWith("System.", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft.", StringComparison.Ordinal);
            var directories = runtime ? new[] { _runtime } : name.Name.StartsWith("Azurite", StringComparison.Ordinal)
                ? new[] { _own } : new[] { _interop, _core };
            var candidates = directories.Select(dir => Path.Combine(dir, name.Name + ".dll")).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length != 1) throw new AssemblyResolutionException(name);
            var assembly = AssemblyDefinition.ReadAssembly(candidates[0], new ReaderParameters
                { AssemblyResolver = this, ReadingMode = ReadingMode.Deferred, ReadSymbols = false });
            if (!string.Equals(assembly.Name.Name, name.Name, StringComparison.Ordinal))
            {
                assembly.Dispose();
                throw new InvalidDataException("Resolved assembly name mismatch: " + name.Name);
            }
            _loaded.Add(name.Name, assembly);
            return assembly;
        }
        public void Dispose()
        {
            foreach (var assembly in _loaded.Values) assembly.Dispose();
            _loaded.Clear();
        }
    }
}
