using System;
using System.IO;

namespace gesh;

static partial class GFiles
{
	// Not Documents/gesh, that's where the source lives on the dev machine.
	static string PlatformFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "gesh-files");
}
