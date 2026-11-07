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
    // AA 1.0.1 was checked against the installed generated APIs and native
    // scheduling handoff. Do not inherit the Fix editor state-machine patches.
    private static readonly HostProfile Release101 = new("1.0.1 (render handoff, FPS and scrolling)", true, false, true, false, false, false, false, false, false);
    internal bool EditorPatches => NativePatches || ProgressiveEditorLoading;


    internal static HostProfile Resolve(string game, string interop, string unity)
    {
        if (Same(game, "BD45C2DFBA4EE59A3A3007E34B53B401985B838D66FFDEBAC863C8527948A80F") &&
            Same(interop, "AFA74D22354E75803E63E8D39F48C4FDE4F13379BA4400BC12C0E880EAEBB11C") &&
            Same(unity, "86FE750B9E5F0B5E5A9FB8E699671036E8EFB8BB05B613AFB4B9BA2DB197B927"))
            return Legacy;
        return Unsupported;
    }

    internal static bool IsPortableCandidate(string game, string metadata) =>
        IsFix(game, metadata) || IsRelease101(game, metadata);

    private static bool IsFix(string game, string metadata) =>
        Same(game, "2B8C36F681A3932071D4E609BB034034B087D4D87DFA88A7FFC1A4A206169528") &&
        Same(metadata, "107C1E0F80C1C6A87CA7239C803DF711ED1DD1F9D2BBAAE0EBF5C9EA9F6078A9");

    private static bool IsRelease101(string game, string metadata) =>
        Same(game, "0776516C59811C724C450E205930546C5D102975F279E33C46C96B878FA06C8B") &&
        Same(metadata, "B4AEEE165E7E10E0F46F85E095FD94487FDB041215F6DCB7AE9DE5D28A364F43");

    internal static HostProfile ResolvePortable(string game, string metadata, bool bindingsVerified) =>
        !bindingsVerified ? Unsupported : IsFix(game, metadata) ? Fix : IsRelease101(game, metadata) ? Release101 : Unsupported;

    private static bool Same(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
