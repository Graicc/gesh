using StereoKit;

namespace gesh;

/// <summary>Asks the headset for a higher display refresh rate than its
/// default 72 Hz, through OpenXR's XR_FB_display_refresh_rate. Smoother motion
/// for hands and the mesh, and gesh's frames are well under the budget, see
/// plans/perf-log.md. Headsets without the extension stay at their default.</summary>
static class GRefreshRate
{
	const string EXTENSION = "XR_FB_display_refresh_rate";

	delegate int XrRequestDisplayRefreshRateFB(ulong session, float displayRefreshRate);

	/// <summary>Call before SK.Initialize.</summary>
	public static void RequestExtension() => Backend.OpenXR.RequestExt(EXTENSION);

	/// <summary>Call after SK.Initialize.</summary>
	public static void Apply(float hz)
	{
		if (Backend.XRType != BackendXRType.OpenXR || !Backend.OpenXR.ExtEnabled(EXTENSION)) return;
		var request = Backend.OpenXR.GetFunction<XrRequestDisplayRefreshRateFB>("xrRequestDisplayRefreshRateFB");
		int result = request?.Invoke(Backend.OpenXR.Session, hz) ?? -1;
		Log.Info($"Requested {hz} Hz display refresh rate: {(result == 0 ? "ok" : $"failed ({result})")}");
	}
}
