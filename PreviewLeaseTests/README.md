# Preview texture lease lifecycle checks

Run `dotnet run --project PreviewLeaseTests/PreviewLeaseTests.csproj -c Release`.

The project compiles the actual production `PreviewTargetLease` and `PreviewResolutionPlan` against deterministic host stubs. It tests binding ownership, stable allocation, lifecycle release, independent camera cadence, descriptor/stack rejection, external rebinds, foreign consumers and export-style restoration. The stubs model native object destruction and count texture allocation/release calls.

These tests cannot establish GPU savings, visual equivalence, actual Unity callback ordering or IL2CPP hook compatibility. Those require host validation. Input and animation leave the lease's `allowed` argument true in the stable-ownership test; the production caller must maintain that contract independently of rendering cadence.
