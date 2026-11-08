namespace Azurite.NormalEditor.Adapter;

// Shared by the actual host finalizer and managed fault-containment tests.
public static class FinalizerDispatch
{
    public static Exception? Complete(AdapterCoordinator coordinator,CallbackKind kind,long instance,AdapterFrame? frame,Exception? originalException)
    {
        coordinator.FinalizerObserved(kind,instance,frame,originalException);
        return originalException;
    }
}
