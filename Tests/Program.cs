using Azurite;

var oldHashes = new[] {
    "BD45C2DFBA4EE59A3A3007E34B53B401985B838D66FFDEBAC863C8527948A80F",
    "AFA74D22354E75803E63E8D39F48C4FDE4F13379BA4400BC12C0E880EAEBB11C",
    "86FE750B9E5F0B5E5A9FB8E699671036E8EFB8BB05B613AFB4B9BA2DB197B927" };
var newHashes = new[] {
    "2B8C36F681A3932071D4E609BB034034B087D4D87DFA88A7FFC1A4A206169528",
    "94CB91F26C01DFD92222BB6F70DA4C951707063C8B5D7D4507EDB9B4304920AC",
    "26E26B2E4BD944F06EF8AD13C0C9C3BB69C1E70109600894178BC835CEDEBA22" };
int passed = 0, failed = 0;
void Check(bool value, string message) {
    if (value) { passed++; Console.WriteLine("PASS " + message); }
    else { failed++; Console.WriteLine("FAIL " + message); }
}
HostProfile Resolve(string[] hashes) => HostProfile.Resolve(hashes[0], hashes[1], hashes[2]);
var legacy = Resolve(oldHashes);
Check(legacy.Supported && legacy.NativePatches && legacy.LegacyExporter &&
    legacy.PreviewOptimization && legacy.LegacyLoadingException, "existing verified host retains its capabilities");
var current = Resolve(newHashes);
Check(current.Supported, "AA 1.0.0-fix can activate basic scrolling and render scheduling");
Check(current.Supported && !current.NativePatches && !current.LegacyExporter &&
    !current.PreviewOptimization && !current.LegacyLoadingException,
    "AA 1.0.0-fix cannot inherit unverified native patches, preview ownership or loading exceptions");
foreach (var hashes in new[] { oldHashes, newHashes }) {
    for (int i = 0; i < 3; i++) {
        var changed = (string[])hashes.Clone(); changed[i] = new string('0', 64);
        Check(!Resolve(changed).Supported, $"reject altered identity component {i} for {hashes[0][..8]}");
    }
}
Check(!HostProfile.Resolve(newHashes[0], oldHashes[1], newHashes[2]).Supported, "reject mixed native and generated bindings");
Check(!HostProfile.Resolve("", "", "").Supported, "reject missing host identity");
Check(HostProfile.Resolve(newHashes[0].ToLowerInvariant(), newHashes[1].ToLowerInvariant(),
    newHashes[2].ToLowerInvariant()).Supported, "hash formatting does not change identity");
Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
