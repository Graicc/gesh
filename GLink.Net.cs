using System.Diagnostics;

namespace gesh;

static partial class GLink
{
	static void PlatformOpen(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
