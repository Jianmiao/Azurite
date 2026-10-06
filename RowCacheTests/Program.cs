using Azurite;
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
var cache = new BoundedRowCache<int, string>(3);
cache.Store(1, "one"); cache.Store(2, "two"); cache.Store(3, "three");
Check(cache.Store(2, "updated") == 0 && cache.Count == 3 && cache.TryGetValue(1, out _), "updating a full cache preserves unrelated rows");
Check(cache.Store(4, "four") == 1 && !cache.TryGetValue(1, out _) && cache.TryGetValue(2, out var value) && value == "updated", "growth evicts only the oldest row");
Check(cache.ResidentNodes == cache.Count, "eviction removes the identity from cleanup storage");
cache.Remove(3);
Check(cache.Count == 2 && cache.ResidentNodes == 2, "native row cleanup promptly drops its value and queue node");
Check(cache.Prune(v => v != "four", 1) == 0 && cache.Count == 2, "cleanup has a fixed observation budget");
Check(cache.Prune(v => v != "four", 1) == 1 && cache.Count == 1 && cache.ResidentNodes == 1, "bounded rotating cleanup eventually removes a dead native row");
cache.Clear();
for (int i = 0; i < 20000; i++) { cache.Store(i, "layout"); cache.Remove(i); }
Check(cache.Count == 0 && cache.ResidentNodes == 0, "repeated list rebuilds retain no historical cleanup identities");
for (int i = 0; i < 20000; i++) cache.Store(i, "layout");
Check(cache.Count == 3 && cache.ResidentNodes == 3, "long-running growth stays within the entry and bookkeeping limit");
Check(cache.Prune(_ => false, 100) == 3 && cache.Count == 0, "cleanup visits each resident at most once per invocation");
cache.Store(1, "layout"); cache.Clear();
Check(cache.Count == 0 && cache.ResidentNodes == 0, "export and disposal clear retained native wrappers");
Console.WriteLine($"RESULT {passed}/{passed}; source-linked bounded row-cache lifetime tests.");
