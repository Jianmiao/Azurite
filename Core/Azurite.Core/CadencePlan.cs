namespace Azurite.Core;

public readonly record struct CadencePlan(bool Valid, int IdleInterval, int DeepInterval, string Reason);
