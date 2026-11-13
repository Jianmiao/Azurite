# Normal Editor shutdown SOURCE checks

This project runs the actual new Entry.Core `CatalogShutdown` and the existing `AdapterCoordinator.Close`. The source-linked `NormalEditor.Adapter.Tests/Fixture.cs` supplies explicitly synthetic native/settings/admission data in a fresh C-only fixture directory. It does not provide production admission or execute AA. Both frozen policy DLLs are referenced as unchanged binary inputs, never rebuilt.

Runtime mode takes one fresh C-only fixture directory. Metadata mode takes `--metadata <compiled-entry-host> <compiled-adapter-host>` and reads the actual PE/IL with Cecil. The latter proves forwarding and guard structure, not host execution or native quiescence.

Close is never triggered by Ready. Success means this managed Catalog producer is stopped and the registered settings coordinator accepted Close. The external integration may then Seal its writer; it still needs real normal shutdown and exit evidence. No test result supplies that evidence.
