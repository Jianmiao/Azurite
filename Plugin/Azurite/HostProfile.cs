using System;

namespace Azurite;

internal sealed record HostProfile(string Name, bool Supported, bool NativePatches, bool FpsLabelPostfix,
    bool PlainTextPatch, bool LegacyExporter, bool PreviewOptimization, bool PreviewOwnership,
    bool LegacyLoadingException, bool ProgressiveEditorLoading)
{
    internal static readonly HostProfile Unsupported = new("unsupported", false, false, false, false, false, false, false, false, false);
    private static readonly HostProfile Legacy = new("1.0-beta", true, true, false, true, true, true, true, true, false);
    // The portable candidate is the only host for which the progressive editor
    // state-machine ABI was inspected. This remains an opt-in feature in Plugin.
    private static readonly HostProfile Fix = new("1.0.0-fix (safe idle surfaces and scrolling)", true, false, true, true, false, true, false, false, true);

    internal static HostProfile Resolve(string game, string interop, string unity)
    {
        if (Same(game, "BD45C2DFBA4EE59A3A3007E34B53B401985B838D66FFDEBAC863C8527948A80F") &&
            Same(interop, "AFA74D22354E75803E63E8D39F48C4FDE4F13379BA4400BC12C0E880EAEBB11C") &&
            Same(unity, "86FE750B9E5F0B5E5A9FB8E699671036E8EFB8BB05B613AFB4B9BA2DB197B927"))
            return Legacy;
        return Unsupported;
    }

    internal static bool IsPortableCandidate(string game, string metadata) =>
        Same(game, "2B8C36F681A3932071D4E609BB034034B087D4D87DFA88A7FFC1A4A206169528") &&
        Same(metadata, "107C1E0F80C1C6A87CA7239C803DF711ED1DD1F9D2BBAAE0EBF5C9EA9F6078A9");

    internal static HostProfile ResolvePortable(string game, string metadata, bool bindingsVerified) =>
        bindingsVerified && IsPortableCandidate(game, metadata) ? Fix : Unsupported;

    private static bool Same(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
