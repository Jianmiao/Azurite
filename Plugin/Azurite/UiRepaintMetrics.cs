namespace Azurite;

internal readonly record struct UiRepaintMetrics(long Generation, long GeometryEvents, long ClipEvents, long TransformChanges, long RegistryChanges, long SceneChanges, int PanelCount, UiDirtyKind LastDirtyKind);
