namespace gesh;

/// <summary>Opens web links in the platform's browser.</summary>
static partial class GLink
{
	public static void Open(string url) => PlatformOpen(url);
}
