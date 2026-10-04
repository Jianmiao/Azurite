namespace Azurite;

internal readonly record struct HostCompatibilitySnapshot(bool Supported, bool ProbeHealthy, bool VideoExportAvailable, bool ExportActive, string Reason)
{
	public static HostCompatibilitySnapshot Unsupported(string reason)
	{
		return new HostCompatibilitySnapshot(Supported: false, ProbeHealthy: false, VideoExportAvailable: false, ExportActive: false, reason);
	}

	public static HostCompatibilitySnapshot Pending(string reason)
	{
		return new HostCompatibilitySnapshot(Supported: true, ProbeHealthy: false, VideoExportAvailable: false, ExportActive: false, reason);
	}

	public static HostCompatibilitySnapshot NoExport(string reason)
	{
		return new HostCompatibilitySnapshot(Supported: true, ProbeHealthy: true, VideoExportAvailable: false, ExportActive: false, reason);
	}
}
