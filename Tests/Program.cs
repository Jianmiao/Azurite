using Azurite;

var oldHashes = new[] {
    "BD45C2DFBA4EE59A3A3007E34B53B401985B838D66FFDEBAC863C8527948A80F",
    "AFA74D22354E75803E63E8D39F48C4FDE4F13379BA4400BC12C0E880EAEBB11C",
    "86FE750B9E5F0B5E5A9FB8E699671036E8EFB8BB05B613AFB4B9BA2DB197B927" };
const string game = "2B8C36F681A3932071D4E609BB034034B087D4D87DFA88A7FFC1A4A206169528";
const string metadata = "107C1E0F80C1C6A87CA7239C803DF711ED1DD1F9D2BBAAE0EBF5C9EA9F6078A9";
int passed = 0, failed = 0;
void Check(bool value, string message) {
    if (value) { passed++; Console.WriteLine("PASS " + message); }
    else { failed++; Console.WriteLine("FAIL " + message); }
}
HostProfile Resolve(string[] hashes) => HostProfile.Resolve(hashes[0], hashes[1], hashes[2]);
var legacy = Resolve(oldHashes);
Check(legacy.Supported && legacy.NativePatches && legacy.LegacyExporter &&
    legacy.PreviewOptimization && legacy.LegacyLoadingException, "existing verified host retains its capabilities");
var current = HostProfile.ResolvePortable(game, metadata, true);
Check(current.Supported, "AA 1.0.0-fix can activate basic scrolling and render scheduling");
Check(current.Supported && !current.NativePatches && !current.LegacyExporter &&
    !current.PreviewOptimization && !current.LegacyLoadingException,
    "AA 1.0.0-fix cannot inherit unverified native patches, preview ownership or loading exceptions");
for (int i = 0; i < 3; i++) {
    var changed = (string[])oldHashes.Clone(); changed[i] = new string('0', 64);
    Check(!Resolve(changed).Supported, $"reject altered legacy identity component {i}");
}
Check(!HostProfile.ResolvePortable(new string('0', 64), metadata, true).Supported, "reject unknown native host despite compatible API");
Check(!HostProfile.ResolvePortable(game, new string('0', 64), true).Supported, "reject mismatched native metadata");
Check(!HostProfile.ResolvePortable(game, metadata, false).Supported, "reject missing or incompatible generated API");
Check(!HostProfile.Resolve(game, oldHashes[1], oldHashes[2]).Supported, "new host cannot skip structural verification using legacy hashes");
Check(!HostProfile.Resolve("", "", "").Supported, "reject missing host identity");
Check(HostProfile.ResolvePortable(game.ToLowerInvariant(), metadata.ToLowerInvariant(), true).Supported, "hash formatting does not change identity");
Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
