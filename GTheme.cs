using System;
using StereoKit;

namespace gesh;

/// <summary>The look of gesh's UI. StereoKit's defaults (1 cm gutters, large
/// text) are made for bigger windows than a menu on the palm, so this
/// tightens them up and sets the accent color. Call once after SK.Initialize.</summary>
static class GTheme
{
	/// <summary>A calm blue, gamma space.</summary>
	public static readonly Color ACCENT = Color.HSV(0.58f, 0.55f, 0.85f);
	/// <summary>A control that will do something destructive on the next tap.</summary>
	public static readonly Color DANGER = Color.HSV(0, 0.65f, 0.9f);
	/// <summary>A text style and a twin of it that's cut off at the clip
	/// region while one is pushed, see GClip. Text is batched per style, so
	/// it can't be clipped draw by draw like the rest of the UI.</summary>
	record Clippable(TextStyle plain, TextStyle clipped, Material clipMaterial);

	static Clippable body = null!, small = null!, mono = null!;

	static Clippable MakeClippable(Font font, float size, Color color)
	{
		Material clipMaterial = Default.MaterialFont.Copy();
		return new Clippable(Text.MakeStyle(font, size, color), Text.MakeStyle(font, size, clipMaterial, color), clipMaterial);
	}

	/// <summary>The usual text, pushed for the whole app.</summary>
	public static TextStyle Body => GClip.Active ? body.clipped : body.plain;
	/// <summary>Small print, for the license list.</summary>
	public static TextStyle Small => GClip.Active ? small.clipped : small.plain;
	/// <summary>Small print in bold, for the name on the info page.
	/// Text styles are made once: each one made holds a GPU material for good,
	/// so making them every frame runs StereoKit out and crashes it.</summary>
	public static TextStyle SmallBold { get; private set; }
	/// <summary>The app's name on the info page.</summary>
	public static TextStyle Title { get; private set; }
	/// <summary>Monospace small print, for the license notices.</summary>
	public static TextStyle Mono => GClip.Active ? mono.clipped : mono.plain;

	/// <summary>Sets the clip region on the clipped text styles.</summary>
	public static void SetTextClip(Vec4 top, Vec4 bottom)
	{
		foreach (Clippable c in new[] { body, small, mono })
		{
			c.clipMaterial["clip_top"] = top;
			c.clipMaterial["clip_bottom"] = bottom;
		}
	}

	/// <summary>Draws an element in a solid color instead of the theme's.
	/// Tinting would multiply the gray buttons into a dark muddy color.</summary>
	public static T Colored<T>(Color? colorGamma, Func<T> element)
	{
		if (!colorGamma.HasValue) return element();
		Color old = UI.GetThemeColor(UIColor.Common);
		UI.SetThemeColor(UIColor.Common, colorGamma.Value);
		try { return element(); }
		finally { UI.SetThemeColor(UIColor.Common, old); }
	}

	public static void Apply()
	{
		// The whole app's UI is the menu, so this text style stays pushed.
		// Pushed before the settings: changing them rebuilds StereoKit's
		// slider meshes, which are sized from the current line height.
		body = MakeClippable(Font.Default, 0.0085f, Color.White);
		small = MakeClippable(Font.Default, 0.0065f, Color.White);
		mono = MakeClippable(Font.FromFamily(OperatingSystem.IsAndroid() ? "monospace" : "Consolas"), 0.0045f, new Color(1, 1, 1, 0.85f));
		UI.PushTextStyle(Body);
		UISettings settings = UI.Settings;
		settings.margin = 0.008f;
		settings.padding = 0.006f;
		settings.gutter = 0.005f;
		UI.Settings = settings;

		UI.ColorScheme = ACCENT;
		Title = Text.MakeStyle(Font.Default, 0.016f, Color.White);
		// The system's own fonts: StereoKit only bundles one regular face.
		SmallBold = Text.MakeStyle(Font.FromFamily(OperatingSystem.IsAndroid() ? "/system/fonts/DroidSans-Bold.ttf" : "Segoe UI Bold"), 0.0065f, Color.White);
	}
}
