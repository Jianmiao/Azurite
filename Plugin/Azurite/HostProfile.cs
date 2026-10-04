using System;

namespace Azurite;

internal sealed record HostProfile(string Name, bool Supported, bool NativePatches,
    bool LegacyExporter, bool PreviewOptimization, bool LegacyLoadingException)
{
    internal static readonly HostProfile Unsupported = new("unsupported", false, false, false, false, false);
    private static readonly HostProfile Legacy = new("1.0-beta", true, true, true, true, true);
    private static readonly HostProfile Fix = new("1.0.0-fix (basic rendering and scrolling)", true, false, false, false, false);

    internal static HostProfile Resolve(string game, string interop, string unity)
    {
        if (Same(game, "BD45C2DFBA4EE59A3A3007E34B53B401985B838D66FFDEBAC863C8527948A80F") &&
            Same(interop, "AFA74D22354E75803E63E8D39F48C4FDE4F13379BA4400BC12C0E880EAEBB11C") &&
            Same(unity, "86FE750B9E5F0B5E5A9FB8E699671036E8EFB8BB05B613AFB4B9BA2DB197B927"))
            return Legacy;
        if (Same(game, "2B8C36F681A3932071D4E609BB034034B087D4D87DFA88A7FFC1A4A206169528") &&
            Same(interop, "94CB91F26C01DFD92222BB6F70DA4C951707063C8B5D7D4507EDB9B4304920AC") &&
            Same(unity, "26E26B2E4BD944F06EF8AD13C0C9C3BB69C1E70109600894178BC835CEDEBA22"))
            return Fix;
        return Unsupported;
    }

    private static bool Same(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
