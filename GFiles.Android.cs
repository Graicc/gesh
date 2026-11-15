using System.IO;

namespace gesh;

static partial class GFiles
{
	/// <summary>The app's external files folder: no permissions needed, and
	/// reachable over USB at Android/data/com.graic.gesh/files/gesh.</summary>
	static string PlatformFolder()
	{
		var external = Android.App.Application.Context.GetExternalFilesDir(null)
			?? throw new IOException("No external files folder");
		return Path.Combine(external.AbsolutePath, "gesh");
	}
}
