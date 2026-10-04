# Portable generated binding contract checks

Run the production-linked mutation checks:

```text
dotnet run --project InteropTests/InteropTests.csproj -c Release
```

Inspect an actual compiled plugin and an installed AA directory without running either:

```text
dotnet run --project InteropTests/InteropTests.csproj -c Release -- <absolute-plugin-dll> <absolute-AA-directory>
```

The validator uses Mono.Cecil metadata readers. It accepts matching APIs even when generated interop MVIDs, PE timestamps or unrelated members differ. It rejects unresolved types/members, changed return/parameter signatures, generic arity, method or field staticness, positive value-type signatures, duplicate method/field signatures, missing files and required Unity/NGUI/IL2CPP inheritance or delegate Invoke changes. The test fixtures use temporary generated assemblies; none is executed.

Resolution is limited to the selected AA's `BepInEx/interop` and `BepInEx/core`, the plugin's own directory, and the currently running .NET framework directory. The helper never falls back to another local AA installation. A separate native GameAssembly and metadata identity gate remains necessary: metadata API agreement does not prove native implementation equivalence, player lifecycle, rendering correctness or performance. TypeRef metadata alone cannot express every expected inheritance relation, so critical required ancestors are explicit rather than claiming arbitrary base-class verification. This is static file compatibility, not a claim about all runtime assembly-loader choices.
