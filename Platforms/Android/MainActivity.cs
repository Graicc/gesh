using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using StereoKit;
using System;
using System.Reflection;
using System.Threading;

namespace gesh;

// Every configuration change is handled in place: Android would otherwise
// destroy and recreate the activity (a keyboard connecting, say), and
// OnDestroy quits StereoKit, which the new activity can't start again.
// Landscape and focus aware are store requirements, see AndroidManifest.xml.
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask, ScreenOrientation = ScreenOrientation.Landscape,
	ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation | ConfigChanges.Touchscreen | ConfigChanges.UiMode | ConfigChanges.Density | ConfigChanges.FontScale | ConfigChanges.Locale | ConfigChanges.LayoutDirection | ConfigChanges.Mcc | ConfigChanges.Mnc)]
[MetaData("com.oculus.vr.focusaware", Value = "true")]
[IntentFilter(new[]{Intent.ActionMain}, Categories = new[]{"org.khronos.openxr.intent.category.IMMERSIVE_HMD", "com.oculus.intent.category.VR", Intent.CategoryLauncher})]
public class MainActivity : Activity, ISurfaceHolderCallback2
{
	View surface;

	protected override void OnCreate(Bundle savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		Run();
	}

	protected override void OnDestroy()
	{
		SK.Quit();
		base.OnDestroy();
	}

	static bool running = false;
	void Run()
	{
		if (running) return;
		running = true;

		// Before anything else, give StereoKit the Activity. This should
		// be set before any other SK calls, otherwise native library
		// loading may fail.
		SK.AndroidActivity = this;

		// Set up a surface for StereoKit to draw on, this is only really
		// important for flatscreen experiences.
		Window.TakeSurface(this);
		Window.SetFormat(Format.Unknown);
		surface = new View(this);
		SetContentView(surface);
		surface.RequestFocus();

		// Task.Run will eat exceptions, but Thread.Start doesn't seem to.
		new Thread(InvokeStereoKit).Start();
	}

	static void InvokeStereoKit()
	{
		MethodInfo entryPoint = typeof(MainActivity).Assembly.EntryPoint
			?? throw new Exception("Couldn't find the app's entry point!");

		// Entry points come in a variety of flavors, parse out how we need to
		// invoke.
		ParameterInfo[] entryParams = entryPoint.GetParameters();
		bool     noArgs  = entryParams.Length == 0;
		bool     hasArgs = entryParams.Length == 1 && entryParams[0].ParameterType == typeof(string[]);
		bool     retVoid = entryPoint.ReturnType == typeof(void);
		bool     retInt  = entryPoint.ReturnType == typeof(int);
		string[] args    = new string[] { };

		// Calling as a delegate allows nice exception bubbling.
		if      (noArgs  && retVoid) ((Action)             Delegate.CreateDelegate(typeof(Action),             entryPoint))();
		else if (noArgs  && retInt ) ((Func<int>)          Delegate.CreateDelegate(typeof(Func<int>),          entryPoint))();
		else if (hasArgs && retVoid) ((Action<string[]>)   Delegate.CreateDelegate(typeof(Action<string[]>),   entryPoint))(args);
		else if (hasArgs && retInt ) ((Func<string[],int>) Delegate.CreateDelegate(typeof(Func<string[],int>), entryPoint))(args);
		else throw new Exception("Couldn't invoke the app's entry point!");

		Process.KillProcess(Process.MyPid());
	}

	// Events related to surface state changes. The Surface is turned into an
	// ANativeWindow here, on the UI thread, while it's sure to be alive.
	// Passing the Surface itself let StereoKit convert it later on its own
	// thread, which crashed when the Surface had been released by then (seen
	// when relaunching quickly).
	public void SurfaceChanged     (ISurfaceHolder holder, [GeneratedEnum] Format format, int width, int height) => SetWindow(holder);
	public void SurfaceCreated     (ISurfaceHolder holder) => SetWindow(holder);
	public void SurfaceDestroyed   (ISurfaceHolder holder) => SK.SetNativeWindow(IntPtr.Zero);

	/// <summary>ANativeWindow_fromSurface gives us a reference. StereoKit
	/// takes its own when handed the window, so ours is released right away.</summary>
	static void SetWindow(ISurfaceHolder holder)
	{
		IntPtr window = ANativeWindow_fromSurface(JNIEnv.Handle, holder.Surface.Handle);
		SK.SetNativeWindow(window);
		if (window != IntPtr.Zero) ANativeWindow_release(window);
	}

	[System.Runtime.InteropServices.DllImport("android")]
	static extern IntPtr ANativeWindow_fromSurface(IntPtr env, IntPtr surface);
	[System.Runtime.InteropServices.DllImport("android")]
	static extern void ANativeWindow_release(IntPtr window);
	public void SurfaceRedrawNeeded(ISurfaceHolder holder) { }
}