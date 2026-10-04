namespace Azurite;

internal enum UiDirtyKind
{
	None,
	Geometry,
	ClipEvent,
	TransformOrClipOffset,
	Registry,
	Scene,
	Failure,
	Disposed
}
