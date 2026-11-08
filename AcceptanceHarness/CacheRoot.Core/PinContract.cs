using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Azurite.CacheRootLab;

public sealed record CodeWindow(string Name, uint Rva, int Size, string Sha256);
public sealed record ManagedModulePin(string FileName, string Sha256, string Version, Guid Mvid);

public static class PinContract
{
    public const string ApprovedLab = @"E:\aamod\labs\Azurite-20261108-editor";
    public const uint UnityImageSize = 0x1e46000, RootRva = 0x1be73a0, ManagerRva = 0x1c98370, CrtRva = 0x1c54cc0, AllocatorRva = 0x1c56248;
    public const string UnitySha256 = "df965f4ba87ce9976d55b0699671779e0ef921f75d5407689ac0ddd514fef1bc";
    public const string PinsSha256 = "8769e65accc4752e2cc5ef6fffc88b48cacbd816b7d290eba4def6fb4070ed69";
    public const string BindingReportSha256 = "22a77398898356018b53f5d0f211b59bfd0ab055956bc65d6f1666d57fbd4974";
    public const string SeedReportSha256 = "92b3d173581ba5cd19ccc1e72677726e5aec775f793856c7f6e0589a729f83d8";
    public static byte[] InitialRoot => Convert.FromHexString("000000000000000000000000000000000000000000000000000000000000001f4900");
    private static readonly JsonDocument Pins = ReadPins();
    public static IReadOnlyList<CodeWindow> CodeWindows { get; } = Array.AsReadOnly(Pins.RootElement.GetProperty("codePins").EnumerateArray()
        .Select(e => new CodeWindow(e.GetProperty("name").GetString()!, Hex(e.GetProperty("rva").GetString()!), e.GetProperty("size").GetInt32(), e.GetProperty("sha256").GetString()!))
        .Append(new CodeWindow("root_converter", 0x4d4160, 0x198, "fc2819cd0227d68c424c14efbbf77b1570a20185b9751bcc3b623aae9146d8f9")).ToArray());
    public static IReadOnlyList<ManagedModulePin> ManagedModules { get; } = Array.AsReadOnly(Pins.RootElement.GetProperty("managedModules").EnumerateArray()
        .Select(e => new ManagedModulePin(Path.GetFileName(e.GetProperty("path").GetString()!), e.GetProperty("sha256").GetString()!, e.GetProperty("assemblyVersion").GetString()!, Guid.Parse(e.GetProperty("mvid").GetString()!))).ToArray());
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static void CheckWindow(CodeWindow window, byte[] bytes)
    {
        if (bytes.Length != window.Size || Hash(bytes) != window.Sha256) throw new InvalidOperationException("Native code window mismatch: " + window.Name);
    }
    public static void VerifyFilesAndMetadata(string installRoot)
    {
        var unity = Path.Combine(installRoot, "UnityPlayer.dll");
        RequireFileHash(unity, UnitySha256);
        using (var stream = File.OpenRead(unity))
        using (var pe = new PEReader(stream))
        {
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 || pe.PEHeaders.PEHeader?.SizeOfImage != UnityImageSize) throw new InvalidOperationException("Unity image machine/size mismatch.");
            foreach (var window in CodeWindows) CheckWindow(window, pe.GetSectionData(checked((int)window.Rva)).GetContent(0, window.Size).ToArray());
            if (!pe.GetSectionData((int)RootRva).GetContent(0, InitialRoot.Length).SequenceEqual(InitialRoot) ||
                !pe.GetSectionData(0x1913400).GetContent(0, 2).SequenceEqual(new byte[] { 9, 0 })) throw new InvalidOperationException("Native file-backed data pin mismatch.");
        }
        foreach (var expected in Pins.RootElement.GetProperty("managedModules").EnumerateArray())
        {
            var file = Path.Combine(installRoot, "BepInEx", "core", Path.GetFileName(expected.GetProperty("path").GetString()!));
            RequireFileHash(file, expected.GetProperty("sha256").GetString()!);
            using var stream = File.OpenRead(file); using var pe = new PEReader(stream); var md = pe.GetMetadataReader();
            if (md.GetGuid(md.GetModuleDefinition().Mvid) != Guid.Parse(expected.GetProperty("mvid").GetString()!) || md.GetAssemblyDefinition().Version.ToString() != expected.GetProperty("assemblyVersion").GetString()) throw new InvalidOperationException("Managed MVID/version drift.");
            foreach (var type in expected.GetProperty("selectedTypes").EnumerateArray())
            {
                var definition = md.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle((int)(Hex(type.GetProperty("token").GetString()!) & 0xffffff)));
                var name = md.GetString(definition.Namespace) + "." + md.GetString(definition.Name);
                if (name != type.GetProperty("name").GetString()) throw new InvalidOperationException("Detour type identity drift.");
                foreach (var method in type.GetProperty("methods").EnumerateArray())
                {
                    var d = md.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle((int)(Hex(method.GetProperty("token").GetString()!) & 0xffffff)));
                    if (md.GetString(d.Name) != method.GetProperty("name").GetString() || Convert.ToHexString(md.GetBlobBytes(d.Signature)).ToLowerInvariant() != method.GetProperty("signatureHex").GetString()) throw new InvalidOperationException("Installed detour signature/token drift.");
                }
            }
        }
        var native = Pins.RootElement.GetProperty("nativeDetourModule");
        var dobby = Path.Combine(installRoot, "BepInEx", "core", "dobby.dll");
        RequireFileHash(dobby, native.GetProperty("sha256").GetString()!);
        if (new FileInfo(dobby).Length != native.GetProperty("size").GetInt64()) throw new InvalidOperationException("Dobby length drift.");
        using var ds = File.OpenRead(dobby); using var dp = new PEReader(ds);
        if (dp.PEHeaders.CoffHeader.Machine != Machine.Amd64 || dp.PEHeaders.PEHeader?.SizeOfImage != Hex(native.GetProperty("sizeOfImage").GetString()!)) throw new InvalidOperationException("Dobby PE identity drift.");
        foreach (var export in native.GetProperty("exports").EnumerateArray())
        {
            var bytes = Convert.FromHexString(export.GetProperty("first32Bytes").GetString()!);
            if (!dp.GetSectionData((int)Hex(export.GetProperty("rva").GetString()!)).GetContent(0, bytes.Length).SequenceEqual(bytes)) throw new InvalidOperationException("Dobby export code pin drift.");
        }
    }
    public static void RequireFileHash(string path, string hash)
    {
        SeedPathPolicy.RequireExistingNoReparse(path);
        if (!string.Equals(Hash(File.ReadAllBytes(path)), hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("File pin mismatch: " + Path.GetFileName(path));
    }
    private static uint Hex(string value) => Convert.ToUInt32(value, 16);
    private static JsonDocument ReadPins()
    {
        var assembly = typeof(PinContract).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("binding-contract-pins.json", StringComparison.Ordinal)))!;
        using var memory = new MemoryStream(); stream.CopyTo(memory); var bytes = memory.ToArray();
        if (Hash(bytes) != PinsSha256) throw new InvalidOperationException("Embedded qualification pins drifted.");
        return JsonDocument.Parse(bytes);
    }
}
