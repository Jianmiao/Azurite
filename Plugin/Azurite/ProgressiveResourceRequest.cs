namespace Azurite;

// Immutable metadata only: no live Script or Unity object is stored in the queue.
internal enum ProgressiveResourceKind { Background, Voice, Popup, Bgm, Sound, Character }
internal readonly record struct ProgressiveResourceRequest(ProgressiveResourceKind Kind, long Id, string Key);
