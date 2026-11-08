namespace Azurite.CacheRootLab;

public enum ConsumerPhase { ClosedPathOnly, OwnedConsumerQualification, NormalCatalog }
public static class ConsumerGuardPolicy
{
    public static void Validate(ConsumerPhase phase, NativeStringValue root, NativeStringValue embeddedPath, string expectedRoot, string admittedCachePath, bool originalBuilderProved, byte readIndex)
    {
        if (phase != ConsumerPhase.OwnedConsumerQualification) throw new InvalidOperationException("Cache initialization is closed for path-only and unqualified normal phases.");
        if (!originalBuilderProved || readIndex > 1 || root.Label != 0x49 || root.Text != expectedRoot ||
            root.Kind is not (NativeStringKind.Inline or NativeStringKind.Owned) || embeddedPath.Kind is not (NativeStringKind.Inline or NativeStringKind.Owned) ||
            embeddedPath.Label != 9 || embeddedPath.Text != admittedCachePath || string.IsNullOrEmpty(admittedCachePath)) throw new InvalidOperationException("Native consumer owner/path/label or builder evidence mismatch.");
    }
}
