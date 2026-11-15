using Android.Content;

namespace gesh;

static partial class GLink
{
	/// <summary>The Quest opens it in its browser, over the app.</summary>
	static void PlatformOpen(string url)
	{
		var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
		intent.AddFlags(ActivityFlags.NewTask);
		Android.App.Application.Context.StartActivity(intent);
	}
}
