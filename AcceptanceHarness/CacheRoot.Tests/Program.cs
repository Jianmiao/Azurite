using System.Runtime.InteropServices;
using System.Text;
using System.Reflection;
using Azurite.CacheRootLab;

int passed = 0, failed = 0;
void Test(string name, Action body) { try { body(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
void Reject(Action body) { try { body(); } catch (InvalidOperationException) { return; } throw new Exception("unsafe input accepted"); }
using var fixtures = new NativeFixtures(); var reader = new NativeStringReader(fixtures);
var empty = fixtures.Inline("", 0x49);
Test("original empty root decodes without reading padding", () => { var s = reader.Read(empty, expectedLabel: 0x49); Check(s.Text == "" && s.Kind == NativeStringKind.Inline && s.ByteLength == 0, "empty root wrong"); });
Test("inline UTF8 path and preserved allocator label", () => { var s = reader.Read(fixtures.Inline("E:/实验/路径", 1), expectedLabel: 1); Check(s.Text == "E:/实验/路径", "UTF8 corrupted"); });
Test("31-byte inline uses tag itself as terminator", () => Check(reader.Read(fixtures.Inline(new string('a', 31), 1)).ByteLength == 31, "inline boundary wrong"));
Test("owned tag5f is accepted by high-bit mask", () => { var s = reader.Read(fixtures.Heap("E:/" + new string('a', 45), 0x5f, 0x49), expectedLabel: 0x49); Check(s.Kind == NativeStringKind.Owned && s.ByteLength == 48, "owned mask wrong"); });
Test("borrowed explicit bytes need neither capacity nor trailing NUL", () => { var s = reader.Read(fixtures.Heap("view", 0xbf, 7, terminate: false, capacity: ulong.MaxValue), admitBorrowed: true); Check(s.Text == "view" && s.Kind == NativeStringKind.Borrowed, "borrowed form wrong"); });
Test("borrowed path representation is refused by protocol", () => Reject(() => reader.Read(fixtures.Heap("view", 0x80, 0x49))));
Test("inline negative length tags rejected", () => { foreach (var tag in new byte[] { 32, 63 }) Reject(() => reader.Read(fixtures.RawTag(tag))); });
Test("reserved tag rejected", () => Reject(() => reader.Read(fixtures.RawTag(0xc0))));
Test("wrong allocator label rejected", () => Reject(() => reader.Read(empty, expectedLabel: 9)));
Test("owned length greater than capacity rejected", () => Reject(() => reader.Read(fixtures.Heap("data", 0x40, 1, capacity: 3))));
Test("oversize path rejected before data copy", () => Reject(() => reader.Read(fixtures.Heap(new string('x', 4097), 0x40, 1))));
Test("owned missing terminator rejected", () => Reject(() => reader.Read(fixtures.Heap("data", 0x40, 1, terminate: false))));
Test("interior NUL rejected", () => Reject(() => reader.Read(fixtures.Inline("a\0b", 1))));
Test("invalid UTF8 rejected", () => { var p = fixtures.Inline("abc", 1); fixtures.Write(p, new byte[] { 0xff }); Reject(() => reader.Read(p)); });
Test("unallocated object read rejected", () => Reject(() => reader.Read(1)));
Test("unreadable owned storage rejected", () => { var p = fixtures.Heap("data", 0x40, 1); fixtures.U64(p, 1); Reject(() => reader.Read(p)); });
Test("pointer overflow rejected", () => { var p = fixtures.Heap("data", 0x40, 1); fixtures.U64(p, ulong.MaxValue - 1); Reject(() => reader.Read(p)); });
Test("owned huge length rejected without integer truncation", () => { var p = fixtures.Heap("data", 0x40, 1); fixtures.U64(p + 16, ulong.MaxValue); Reject(() => reader.Read(p)); });
Test("decoder copies value and does not retain borrowed storage", () => { var p = fixtures.Heap("original", 0x80, 1, terminate: false); var value = reader.Read(p, true); fixtures.Write(fixtures.PointerAt(p), Encoding.UTF8.GetBytes("modified")); Check(value.Text == "original", "value aliases native memory"); });
var processMemory = new CurrentProcessMemory();
Test("actual current-process reader reads allocated memory only", () => Check(new NativeStringReader(processMemory).Read(empty, expectedLabel: 0x49).Text == "", "actual reader failed"));
Test("VirtualQuery rejects real uncommitted/no-access fixture page", () =>
{
    var p = NativePages.VirtualAlloc(IntPtr.Zero, 4096, 0x3000, 0x01); Check(p != IntPtr.Zero, "VirtualAlloc failed");
    try { Reject(() => processMemory.Read((ulong)p.ToInt64(), 34)); } finally { NativePages.VirtualFree(p, UIntPtr.Zero, 0x8000); }
});
Test("real memory range crossing into no-access page is rejected", () =>
{
    var p = NativePages.VirtualAlloc(IntPtr.Zero, 8192, 0x3000, 0x04); Check(p != IntPtr.Zero, "VirtualAlloc failed");
    try { Check(NativePages.VirtualProtect(p + 4096, 4096, 0x01, out _), "VirtualProtect failed"); Reject(() => processMemory.Read((ulong)p.ToInt64() + 4080, 34)); }
    finally { NativePages.VirtualFree(p, UIntPtr.Zero, 0x8000); }
});
Test("data allocation cannot masquerade as executable target", () => Reject(() => processMemory.RequireExecutable(empty, 8)));
Test("allocated fixture cannot masquerade as a loaded PE image", () => Reject(() => processMemory.RequireModuleImage(empty, 34, empty)));
Test("native result slot is aligned with untouched diagnostic padding", () =>
{
    using var slot = new NativeResultStorage(); Check(slot.Address % 16 == 0 && slot.Address != 0 && slot.CanariesIntact(processMemory), "invalid guarded result storage");
    Check(processMemory.Read(slot.Address + 0x22, 6).All(b => b == 0xcc), "slot padding manufactured as native fields");
});
Test("native result overrun canary fails before any destructor permission", () =>
{
    using var slot = new NativeResultStorage(); Check(slot.Address != 0 && slot.AllocationBytes >= 0x28 + 32, "result slot was not allocated"); Marshal.WriteByte((IntPtr)(long)(slot.Address + 0x28), 0);
    Check(!slot.CanariesIntact(processMemory), "overrun was not caught");
});

const string root = "E:/lab/run/unity-knownfolder";
NativeStringValue Value(string text, ushort label) => new(text, NativeStringKind.Owned, 0x5f, label, 1024, Encoding.UTF8.GetByteCount(text));
ProbeLedger Armed(CacheProbeMode mode = CacheProbeMode.SeededPathOnly) { var l = new ProbeLedger(mode, root, 42); l.Admit(new(2, 100, 0, true, 42)); return l; }
ProbeLedger Seeded() { var l = Armed(); l.BeginSeed(); l.FinishSeed(Value(root, 0x49), false, 0); return l; }
Test("normal mode cannot borrow path-only admission", () => Reject(() => Armed(CacheProbeMode.NormalCatalog)));
Test("null manager and empty root do not replace startup identity", () => { var l = new ProbeLedger(CacheProbeMode.SeededPathOnly, root, 42); Reject(() => l.Admit(new(1, 100, 0, true, 42))); });
Test("wrong OS thread rejected", () => { var l = new ProbeLedger(CacheProbeMode.SeededPathOnly, root, 42); Reject(() => l.Admit(new(2, 100, 0, true, 43))); });
Test("populated root rejected even if request path matches", () => { var l = new ProbeLedger(CacheProbeMode.SeededPathOnly, root, 42); Reject(() => l.Admit(new(2, 100, 0, false, 42))); });
Test("published cache manager rejected", () => { var l = new ProbeLedger(CacheProbeMode.SeededPathOnly, root, 42); Reject(() => l.Admit(new(2, 100, 1, true, 42))); });
Test("no-op seed rejected before builder", () => { var l = Armed(); l.BeginSeed(); Reject(() => l.FinishSeed(Value("", 0x49), false, 0)); Check(l.Stage == ProbeStage.Failed, "failure not latched"); });
Test("seed input alias rejected", () => { var l = Armed(); l.BeginSeed(); Reject(() => l.FinishSeed(Value(root, 0x49), true, 0)); });
Test("seed cannot be retried", () => Reject(() => Seeded().BeginSeed()));
Test("builder cannot skip positive seed proof", () => Reject(() => Armed().BeginBuilder()));
Test("wrong result pointer prevents destructor admission", () => { var l = Seeded(); l.BeginBuilder(); Reject(() => l.ValidateResult(false, true, Value(root + "/Unity", 1), Value(root, 0x49), 0)); Reject(l.Destroyed); });
Test("canary corruption prevents destructor admission", () => { var l = Seeded(); l.BeginBuilder(); Reject(() => l.ValidateResult(true, false, Value(root + "/Unity", 1), Value(root, 0x49), 0)); });
Test("result label1 differs from root49 and cache9", () => { var l = Seeded(); l.BeginBuilder(); Reject(() => l.ValidateResult(true, true, Value(root + "/Unity", 9), Value(root, 0x49), 0)); });
Test("seeded original-builder path proves local result then exactly one destroy", () => { var l = Seeded(); l.BeginBuilder(); l.ValidateResult(true, true, Value(root + "/Unity", 1), Value(root, 0x49), 0); l.Destroyed(); l.Complete(); Check(l.Stage == ProbeStage.Complete, "not complete"); Reject(l.Destroyed); });
Test("missed seeder cannot pass with ordinary LocalLow result", () => { var l = Seeded(); l.BeginBuilder(); Reject(() => l.ValidateResult(true, true, Value("C:/ordinary/Unity", 1), Value("C:/ordinary", 0x49), 0)); });
Test("omitted control permits only untouched original-builder observation", () => { var l = Armed(CacheProbeMode.OmittedSeedPathOnly); l.BeginBuilder(); l.ValidateResult(true, true, Value("C:/ordinary/Unity", 1), Value("C:/ordinary", 0x49), 0); l.Destroyed(); l.Complete(); Check(l.Stage == ProbeStage.Complete, "negative control incomplete"); });
Test("omitted control cannot invoke helper", () => Reject(() => Armed(CacheProbeMode.OmittedSeedPathOnly).BeginSeed()));
Test("one-shot child cannot run second builder", () => { var l = Seeded(); l.BeginBuilder(); Reject(l.BeginBuilder); });
Test("closed path-only phase refuses every cache initializer", () => { foreach (byte readIndex in new byte[] { 0, 1 }) Reject(() => ConsumerGuardPolicy.Validate(ConsumerPhase.ClosedPathOnly, Value(root, 0x49), Value(root + "/Unity", 9), root, root + "/Unity", true, readIndex)); });
Test("owned consumer guard admits both native byte branches without changing them", () => { foreach (byte readIndex in new byte[] { 0, 1 }) ConsumerGuardPolicy.Validate(ConsumerPhase.OwnedConsumerQualification, Value(root, 0x49), Value(root + "/Unity", 9), root, root + "/Unity", true, readIndex); });
Test("consumer requires original builder evidence", () => Reject(() => ConsumerGuardPolicy.Validate(ConsumerPhase.OwnedConsumerQualification, Value(root, 0x49), Value(root + "/Unity", 9), root, root + "/Unity", false, 1)));
Test("consumer refuses second disposable lab root", () => Reject(() => ConsumerGuardPolicy.Validate(ConsumerPhase.OwnedConsumerQualification, Value(root, 0x49), Value("E:/other-lab/Unity", 9), root, root + "/Unity", true, 1)));
Test("normal consumer is not opened by a qualification success flag", () => Reject(() => ConsumerGuardPolicy.Validate(ConsumerPhase.NormalCatalog, Value(root, 0x49), Value(root + "/Unity", 9), root, root + "/Unity", true, 1)));
Test("seed preserves Unicode while normalizing native separators", () => Check(SeedPathPolicy.ValidateText(@"E:\lab\实验\unity-knownfolder") == "E:/lab/实验/unity-knownfolder", "wrong normalized seed"));
Test("seed rejects isolated UTF16 surrogate before native converter", () => Reject(() => SeedPathPolicy.ValidateText("E:\\lab\\\ud800")));
Test("seed rejects embedded NUL", () => Reject(() => SeedPathPolicy.ValidateText("E:\\lab\\a\0b")));
Test("seed rejects relative, traversal, UNC and streams", () => { foreach(var path in new[] { "relative", @"E:\lab\..\other", @"\\host\share", @"E:\lab\x:hidden" }) Reject(() => SeedPathPolicy.ValidateText(path)); });
Test("seed UTF8 bound is independent of UTF16 character count", () => Reject(() => SeedPathPolicy.ValidateText("E:\\" + new string('界', 1400))));
Test("seed reserves bytes for the original Unity suffix before native entry", () => Reject(() => SeedPathPolicy.ValidateText("E:\\" + new string('a', 4088))));
void Abi(Type type, Type result, params Type[] parameters)
{
    var method = type.GetMethod("Invoke")!;
    Check(method.ReturnType == result && method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters), "native ABI parameter/result drift");
    Check(type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>()?.CallingConvention == CallingConvention.Cdecl, "native calling convention drift");
}
Test("seed helper forward ABI is exactly void pointer pointer", () => Abi(typeof(NativeAbi.SeedRootEntry), typeof(void), typeof(IntPtr), typeof(IntPtr)));
Test("original builder uses result slot suffix span and byte without MethodInfo", () => Abi(typeof(NativeAbi.BuildPathEntry), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(byte)));
Test("local constructor and destructor signatures preserve lifetime labels", () => { Abi(typeof(NativeAbi.ConstructStringEntry), typeof(IntPtr), typeof(IntPtr), typeof(ushort)); Abi(typeof(NativeAbi.DestroyStringEntry), typeof(void), typeof(IntPtr)); });
Test("initializer ABI covers raw Cache pointer and byte result", () => Abi(typeof(NativeAbi.CacheInitializeEntry), typeof(byte), typeof(IntPtr), typeof(byte)));
Test("actual live entry refuses this harmless non-AA test process before target calls", () => Reject(() => PreparedPathProbe.Prepare(new CacheProbeRequest("unused", new string('0', 64), CacheProbeMode.SeededPathOnly), null!, _ => {})));
Test("compiled code pin rejects changed or truncated bytes", () => { var pin = new CodeWindow("fixture", 0, 3, "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81"); PinContract.CheckWindow(pin, new byte[] { 1, 2, 3 }); Reject(() => PinContract.CheckWindow(pin, new byte[] { 1, 2, 4 })); Reject(() => PinContract.CheckWindow(pin, new byte[] { 1, 2 })); });
if (args.Length == 2 && args[0] == "--host") Test("current host files native windows and actual metadata signatures match", () => PinContract.VerifyFilesAndMetadata(args[1]));
Console.WriteLine($"RESULT passed={passed} failed={failed}; allocated fixture memory only; no Unity/AA code executed");
return failed == 0 ? 0 : 1;

internal sealed class NativeFixtures : IBoundedMemory, IDisposable
{
    private readonly List<(IntPtr Pointer, int Count)> blocks = new();
    public ulong Allocate(int count) { var p = Marshal.AllocHGlobal(count); blocks.Add((p, count)); Marshal.Copy(new byte[count], 0, p, count); return (ulong)p.ToInt64(); }
    public byte[] Read(ulong address, int count)
    {
        if (count < 0 || address > ulong.MaxValue - (ulong)count || !blocks.Any(b => address >= (ulong)b.Pointer.ToInt64() && address + (ulong)count <= (ulong)b.Pointer.ToInt64() + (ulong)b.Count)) throw new InvalidOperationException("unreadable fixture range");
        var bytes = new byte[count]; Marshal.Copy((IntPtr)(long)address, bytes, 0, count); return bytes;
    }
    public void Write(ulong p, byte[] data) => Marshal.Copy(data, 0, (IntPtr)(long)p, data.Length);
    public void U64(ulong p, ulong value) => Write(p, BitConverter.GetBytes(value));
    public ulong PointerAt(ulong p) => BitConverter.ToUInt64(Read(p, 8));
    public ulong Inline(string text, ushort label) { var p = Allocate(0x22); var b = Encoding.UTF8.GetBytes(text); if (b.Length > 31) throw new Exception("bad fixture"); Write(p, b); Write(p + 31, new[] { (byte)(31 - b.Length) }); Write(p + 32, BitConverter.GetBytes(label)); return p; }
    public ulong RawTag(byte tag) { var p = Allocate(0x22); Write(p + 31, new[] { tag }); return p; }
    public ulong Heap(string text, byte tag, ushort label, bool terminate = true, ulong? capacity = null)
    {
        var b = Encoding.UTF8.GetBytes(text); var data = Allocate(b.Length + (terminate ? 1 : 0)); Write(data, b); var p = Allocate(0x22);
        U64(p, data); U64(p + 8, capacity ?? (ulong)b.Length); U64(p + 16, (ulong)b.Length); Write(p + 31, new[] { tag }); Write(p + 32, BitConverter.GetBytes(label)); return p;
    }
    public void Dispose() { foreach (var b in blocks) Marshal.FreeHGlobal(b.Pointer); blocks.Clear(); }
}

internal static class NativePages
{
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint type, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint oldProtection);
}
