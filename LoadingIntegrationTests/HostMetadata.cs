using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Read metadata only. Never load the native runtime or launch AA.
internal sealed class HostMetadata : IDisposable
{
    private readonly FileStream stream;
    private readonly PEReader pe;
    private readonly MetadataReader reader;
    private readonly Names names;
    private readonly Dictionary<string, TypeDefinitionHandle> types = new();
    public int VerifiedMethods { get; private set; }

    public HostMetadata(string assemblyPath)
    {
        stream = File.OpenRead(assemblyPath);
        pe = new PEReader(stream);
        reader = pe.GetMetadataReader();
        names = new Names();
        foreach (var handle in reader.TypeDefinitions)
            types[names.GetTypeFromDefinition(reader, handle, 0)] = handle;
    }

    public void Verify(MethodInfo stub)
    {
        string type = stub.DeclaringType.FullName;
        if (!types.TryGetValue(type, out var handle))
            throw new MissingMemberException("Installed host type missing: " + type);
        string[] parameters = stub.GetParameters().Select(p => TypeName(p.ParameterType)).ToArray();
        foreach (var methodHandle in reader.GetTypeDefinition(handle).GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != stub.Name) continue;
            var signature = method.DecodeSignature(names, (object)null);
            if (!signature.ParameterTypes.SequenceEqual(parameters)) continue;
            if (signature.ReturnType != TypeName(stub.ReturnType))
                throw new InvalidOperationException("Host return type mismatch: " + stub);
            if (((method.Attributes & MethodAttributes.Static) != 0) != stub.IsStatic)
                throw new InvalidOperationException("Host static/instance mismatch: " + stub);
            VerifiedMethods++;
            return;
        }
        throw new MissingMethodException("Installed host signature missing: " + type + "." + stub.Name + "(" + string.Join(",", parameters) + ")");
    }

    public void VerifyProperty(Type stubType, string name)
    {
        var stub = stubType.GetProperty(name) ?? throw new MissingMemberException(stubType.FullName, name);
        Verify(stub.GetMethod);
        if (stub.SetMethod != null) Verify(stub.SetMethod);
    }

    public void Dispose() { pe.Dispose(); stream.Dispose(); }

    private static string TypeName(Type type) => type.IsGenericType
        ? type.GetGenericTypeDefinition().FullName + "[" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + "]"
        : type.FullName;

    private sealed class Names : ISignatureTypeProvider<string, object>
    {
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
        {
            var d = r.GetTypeDefinition(h);
            if (!d.GetDeclaringType().IsNil) return GetTypeFromDefinition(r, d.GetDeclaringType(), k) + "+" + r.GetString(d.Name);
            string ns = r.GetString(d.Namespace);
            return (ns.Length == 0 ? "" : ns + ".") + r.GetString(d.Name);
        }
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
        {
            var d = r.GetTypeReference(h);
            if (d.ResolutionScope.Kind == HandleKind.TypeReference) return GetTypeFromReference(r, (TypeReferenceHandle)d.ResolutionScope, k) + "+" + r.GetString(d.Name);
            string ns = r.GetString(d.Namespace);
            return (ns.Length == 0 ? "" : ns + ".") + r.GetString(d.Name);
        }
        public string GetPrimitiveType(PrimitiveTypeCode c) => "System." + (c switch { PrimitiveTypeCode.Boolean => "Boolean", PrimitiveTypeCode.Void => "Void", PrimitiveTypeCode.String => "String", PrimitiveTypeCode.Object => "Object", _ => c.ToString() });
        public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
        public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
        public string GetByReferenceType(string e) => e + "&";
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
        public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "[" + string.Join(",", a) + "]";
        public string GetGenericMethodParameter(object c, int i) => "!!" + i;
        public string GetGenericTypeParameter(object c, int i) => "!" + i;
        public string GetModifiedType(string m, string e, bool r) => e;
        public string GetPinnedType(string e) => e;
        public string GetPointerType(string e) => e + "*";
        public string GetSZArrayType(string e) => e + "[]";
    }
}
