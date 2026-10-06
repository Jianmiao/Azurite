# Driver lifecycle regression

`dotnet run --project DriverTests/DriverTests.csproj -c Release -p:AAInstallPath=F:\AzureArchive_100_fix`

Links the actual AzuriteDriver source, invokes simulated Unity lifecycle messages,
and verifies terminal quit is distinct from a live update failure. Checks six
editor-profiler target signatures and the existing catalog-metrics field getter
against installed AA/Unity metadata. It does not launch AA or initialize IL2CPP.

The installed Il2CppInterop ClassInjector enumerates declared private and public
instance methods, so the private parameterless OnApplicationQuit callback follows
the same injection convention as Update/OnDestroy. Actual Unity callback ordering
still requires the user runtime check; these tests do not reproduce it.
