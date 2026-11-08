using System.Buffers.Binary;
using System.Text;

namespace Azurite.CacheRootLab;

public enum NativeStringKind : byte { Inline = 0, Owned = 0x40, Borrowed = 0x80 }
public sealed record NativeStringValue(string Text, NativeStringKind Kind, byte Tag, ushort Label, ulong DataAddress, int ByteLength);
public interface IBoundedMemory { byte[] Read(ulong address, int count); }
public sealed class NativeStringReader
{
    public const int SlotSize = 0x28, MeaningfulSize = 0x22, MaximumBytes = 4096;
    private readonly IBoundedMemory memory;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public NativeStringReader(IBoundedMemory memory) => this.memory = memory;
    public NativeStringValue Read(ulong address, bool admitBorrowed = false, ushort? expectedLabel = null)
    {
        MemoryRange.Check(address, MeaningfulSize);
        var header = memory.Read(address, MeaningfulSize); // Never inspect uninitialized six-byte slot padding.
        var tag = header[31]; var kind = (NativeStringKind)(tag & 0xc0);
        var label = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32, 2));
        if (expectedLabel.HasValue && label != expectedLabel.Value) throw new InvalidOperationException("Native allocator label mismatch.");
        ulong length, pointer;
        if (kind == NativeStringKind.Inline)
        {
            if (tag > 31) throw new InvalidOperationException("Invalid inline tag/length.");
            length = (ulong)(31 - tag); pointer = address;
        }
        else if (kind == NativeStringKind.Owned || kind == NativeStringKind.Borrowed)
        {
            if (kind == NativeStringKind.Borrowed && !admitBorrowed) throw new InvalidOperationException("Borrowed native path is outside the qualified protocol.");
            pointer = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(0, 8));
            length = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(16, 8));
            if (kind == NativeStringKind.Owned)
            {
                var capacity = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8, 8));
                if (length > capacity || capacity == ulong.MaxValue) throw new InvalidOperationException("Owned length/capacity invalid.");
                MemoryRange.Check(pointer, capacity + 1);
            }
        }
        else throw new InvalidOperationException("Reserved native string kind.");
        if (length > MaximumBytes) throw new InvalidOperationException("Native string exceeds bounded path observation.");
        var count = checked((int)length);
        var terminated = kind != NativeStringKind.Borrowed;
        MemoryRange.Check(pointer, (ulong)count + (terminated ? 1UL : 0UL));
        var bytes = count == 0 && !terminated ? Array.Empty<byte>() : memory.Read(pointer, count + (terminated ? 1 : 0));
        if (terminated && bytes[count] != 0) throw new InvalidOperationException("Native string has no expected terminator.");
        if (bytes.AsSpan(0, count).Contains((byte)0)) throw new InvalidOperationException("Native path contains an interior NUL.");
        try { return new NativeStringValue(StrictUtf8.GetString(bytes, 0, count), kind, tag, label, pointer, count); }
        catch (DecoderFallbackException e) { throw new InvalidOperationException("Invalid native UTF-8.", e); }
    }
}

public static class MemoryRange
{
    public static void Check(ulong address, ulong count)
    {
        if (address == 0 || address > long.MaxValue || count > (ulong)long.MaxValue - address) throw new InvalidOperationException("Invalid/overflowing user memory range.");
    }
    public static bool Overlaps(ulong a, ulong aLength, ulong b, ulong bLength)
    {
        Check(a, aLength); Check(b, bLength);
        return a < b + bLength && b < a + aLength;
    }
}
