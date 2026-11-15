using System;
using System.Collections.Generic;
using StereoKit;

namespace gesh;

/// <summary>The palm menu's options for the paint tools: brush size,
/// pipette, and color (wheel, brightness, presets, recent colors). Settings
/// live in GSettings, see plans/paint.md. Rows are a gutter apart, top to
/// bottom.</summary>
class GPaintTab
{
	public static readonly Color32[] PRESETS =
	[
		new(255, 255, 255, 255), new(128, 128, 128, 255), new(20, 20, 20, 255), new(140, 90, 50, 255), new(230, 60, 50, 255), new(245, 145, 40, 255),
		new(250, 215, 60, 255), new(90, 190, 80, 255), new(40, 160, 150, 255), new(60, 120, 220, 255), new(120, 80, 200, 255), new(220, 90, 170, 255),
	];

	const float WHEEL = 0.1f;
	const float BAR = 0.022f;
	const float TOOL = GPalmMenu.BUTTON;
	const float MIN_RADIUS = 0.005f;
	const float MAX_RADIUS = 0.06f;
	/// <summary>Slider range with values growing right and up.</summary>
	static readonly Vec2 UP_MIN = new(0, 1), UP_MAX = new(1, 0);

	/// <summary>A swatch fills a sixth of a row.</summary>
	static float Swatch => (GPalmMenu.WIDTH - UI.Settings.margin * 2 - UI.Settings.gutter * 5) / 6;

	/// <summary>The panel's height: the top row, the wheel with the
	/// brightness bar beside it, two rows of presets and one of recent colors.</summary>
	public static float Height => UI.Settings.margin * 2 + TOOL + WHEEL + Swatch * 3 + UI.Settings.gutter * 4;

	/// <summary>The wheel and brightness bar edit hue, saturation and
	/// brightness separately, so the hue isn't lost at zero brightness or
	/// saturation. Synced from the paint color whenever that changed some
	/// other way (presets, picking a color off the mesh).</summary>
	Vec3 hsv;
	Color32? hsvOf;

	readonly Tex wheel = WheelTexture();
	readonly Tex brightness = new(TexType.ImageNomips | TexType.Dynamic, TexFormat.Rgba32) { AddressMode = TexAddress.Clamp };
	Vec2 brightnessFor = new(-1, -1);

	public void Draw(GSettings settings)
	{
		if (!(hsvOf.HasValue && Same(hsvOf.Value, settings.brushColor)))
		{
			Vec3 derived = Gamma(settings.brushColor).ToHSV();
			// Keep the hue if the new color has none (grays).
			hsv = derived.y == 0 ? new Vec3(hsv.x, 0, derived.z) : derived;
			hsvOf = settings.brushColor;
		}

		DrawTopRow(settings);
		DrawColorPicker(settings);
		DrawSwatches(settings, "preset", PRESETS);
		DrawSwatches(settings, "recent", settings.recentColors, GSettings.RECENT_COLORS);
	}

	/// <summary>One row, centered on one line: the tool as it looks on the
	/// fingertip (color, and the brush's size), the size slider, and the
	/// pipette.</summary>
	static void DrawTopRow(GSettings settings)
	{
		Bounds b = UI.LayoutReserve(new Vec2(TOOL, TOOL));
		Color color = Linear(settings.brushColor);
		if (settings.tool == GTool.Brush)
		{
			float size = float.Lerp(0.01f, TOOL, (settings.brushRadius - MIN_RADIUS) / (MAX_RADIUS - MIN_RADIUS));
			GDraw.Shape(GIcon.Disc, b.center, new Vec2(size, size), color);
		}
		else if (settings.tool == GTool.Fill)
		{
			GDraw.Shape(GIcon.Square, b.center, new Vec2(0.028f, 0.028f), color);
		}
		else throw new InvalidOperationException($"{settings.tool} isn't a paint tool");
		UI.SameLine();

		// Only the brush has a size.
		UI.PushEnabled(settings.tool == GTool.Brush && !settings.picking);
		GDraw.HSlider("brushRadius", ref settings.brushRadius, MIN_RADIUS, MAX_RADIUS, UI.LayoutReserve(new Vec2(UI.LayoutRemaining.x - TOOL - UI.Settings.gutter, TOOL)));
		UI.PopEnabled();
		UI.SameLine();

		Sprite pipette = GIcons.Get(GIcon.Pipette);
		bool picking = settings.picking;
		if (GTheme.Colored(picking ? GTheme.ACCENT : null, () => UI.Toggle("pipette", ref picking, pipette, UIBtnLayout.CenterNoText, new Vec2(TOOL, TOOL)))) settings.picking = picking;
	}

	void DrawColorPicker(GSettings settings)
	{
		// Wheel: hue around, saturation out from the center. A 2D slider,
		// clamped to the disc, centered. It and the bar take the color
		// wherever the finger touches them, not only at the knob. StereoKit's
		// slider y grows down, so min and max y are swapped to make values
		// grow up, like the knobs are drawn.
		//
		// The row is the wheel, centered in the space left of the pipette's
		// column, and the brightness bar, centered in that column under the
		// pipette. Layout x grows to the user's left.
		Bounds row = UI.LayoutReserve(new Vec2(UI.LayoutRemaining.x, WHEEL));
		float left = GDraw.TopLeft(row).x;
		float right = left - row.dimensions.x;
		float wheelSpace = row.dimensions.x - TOOL - UI.Settings.gutter;
		Bounds wb = new(new Vec3(left - wheelSpace / 2, row.center.y, row.center.z), new Vec3(WHEEL, WHEEL, 0));
		Bounds bb = new(new Vec3(right + TOOL / 2, row.center.y, row.center.z), new Vec3(BAR, WHEEL, 0));
		float angle = hsv.x * MathF.Tau;
		Vec2 wheelValue = new Vec2(0.5f, 0.5f) + new Vec2(MathF.Cos(angle), MathF.Sin(angle)) * hsv.y * 0.5f;
		UI.SliderBehavior(GDraw.TopLeft(wb), new Vec2(WHEEL, WHEEL), UI.StackHash("wheel"), ref wheelValue, UP_MIN, UP_MAX, Vec2.Zero, Vec2.Zero, UIConfirm.Push, out UISliderData wheelData);
		if (wheelData.activeState.IsActive())
		{
			Vec2 d = (wheelValue - new Vec2(0.5f, 0.5f)) * 2;
			hsv = new Vec3((MathF.Atan2(d.y, d.x) / MathF.Tau + 1) % 1, float.Min(d.Length, 1), hsv.z);
			Apply(settings);
		}
		GDraw.Texture(wheel, wb.center, new Vec2(WHEEL, WHEEL), new Color(hsv.z, hsv.z, hsv.z).ToLinear());
		Vec2 knobValue = new Vec2(0.5f, 0.5f) + new Vec2(MathF.Cos(hsv.x * MathF.Tau), MathF.Sin(hsv.x * MathF.Tau)) * hsv.y * 0.5f;
		Knob(wb, knobValue, settings.brushColor);

		// Brightness: a vertical bar showing the gradient from black at the
		// bottom to the fully bright color at the top.
		Vec2 barValue = new(0.5f, hsv.z);
		UI.SliderBehavior(GDraw.TopLeft(bb), new Vec2(BAR, WHEEL), UI.StackHash("brightness"), ref barValue, UP_MIN, UP_MAX, Vec2.Zero, Vec2.Zero, UIConfirm.Push, out UISliderData barData);
		if (barData.activeState.IsActive())
		{
			hsv = new Vec3(hsv.x, hsv.y, float.Clamp(barValue.y, 0, 1));
			Apply(settings);
		}
		UpdateBrightnessTexture();
		GDraw.Texture(brightness, bb.center, new Vec2(BAR * 0.6f, WHEEL), Color.White);
		Knob(bb, new Vec2(0.5f, hsv.z), settings.brushColor);
	}

	/// <summary>A round handle in the current color, at a slider value.
	/// StereoKit's layout x grows to the left; values grow right and up.</summary>
	static void Knob(Bounds b, Vec2 value, Color32 color)
	{
		Vec3 at = new(b.center.x + b.dimensions.x / 2 - value.x * b.dimensions.x, b.center.y - b.dimensions.y / 2 + value.y * b.dimensions.y, b.center.z);
		GDraw.Shape(GIcon.Disc, at, new Vec2(0.014f, 0.014f), Linear(color), layer: 1);
		GDraw.Shape(GIcon.Ring, at, new Vec2(0.016f, 0.016f), Color.White, layer: 2);
	}

	/// <summary>Color swatches, six to a row, sized to fill it.</summary>
	/// <param name="slots">At least this many places, the empty ones dim,
	/// so the panel doesn't change size as colors are used.</param>
	static void DrawSwatches(GSettings settings, string id, IReadOnlyList<Color32> colors, int slots = 0)
	{
		float swatch = Swatch;
		for (int i = 0; i < int.Max(colors.Count, slots); i++)
		{
			if (i % 6 != 0) UI.SameLine();
			Bounds b = UI.LayoutReserve(new Vec2(swatch, swatch));
			if (i >= colors.Count)
			{
				GDraw.Shape(GIcon.Square, b.center, new Vec2(swatch, swatch) * 0.92f, new Color(1, 1, 1, 0.08f));
				continue;
			}
			Color32 color = colors[i];
			UI.ButtonBehavior(GDraw.TopLeft(b), new Vec2(swatch, swatch), $"{id}{i}", out float fingerOffset, out BtnState state, out BtnState focus);
			if (state.IsJustActive()) settings.brushColor = color;

			// The swatch is the button: it sinks when pushed. The current color
			// is framed in white.
			float lift = float.Max(0, UI.Settings.depth * 0.5f - fingerOffset);
			if (Same(color, settings.brushColor)) GDraw.Shape(GIcon.Square, b.center, new Vec2(swatch, swatch) * 1.12f, Color.White, lift: lift);
			GDraw.Shape(GIcon.Square, b.center, new Vec2(swatch, swatch) * (focus.IsActive() ? 1.0f : 0.92f), Linear(color), layer: 1, lift: lift);
		}
	}

	void Apply(GSettings settings)
	{
		settings.brushColor = Color.HSV(hsv.x, hsv.y, hsv.z).ToColor32Sat();
		hsvOf = settings.brushColor;
	}

	void UpdateBrightnessTexture()
	{
		Vec2 hs = new(hsv.x, hsv.y);
		if (hs.x == brightnessFor.x && hs.y == brightnessFor.y) return;
		brightnessFor = hs;
		var pixels = new Color32[64];
		// The texture's top row is the bright end.
		for (int i = 0; i < pixels.Length; i++) pixels[pixels.Length - 1 - i] = Color.HSV(hsv.x, hsv.y, i / (pixels.Length - 1f)).ToColor32Sat();
		brightness.SetColors(1, pixels.Length, pixels);
	}

	/// <summary>Hue around the center, saturation outward, full brightness,
	/// with an anti-aliased round edge.</summary>
	static Tex WheelTexture()
	{
		const int size = 128;
		var pixels = new Color32[size * size];
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				// Texture u grows right, v grows down; slider values grow right and up.
				Vec2 d = new Vec2((x + 0.5f) / size - 0.5f, 0.5f - (y + 0.5f) / size) * 2;
				float r = d.Length;
				float hue = (MathF.Atan2(d.y, d.x) / MathF.Tau + 1) % 1;
				Color32 c = Color.HSV(hue, float.Min(r, 1), 1).ToColor32Sat();
				c.a = (byte)(float.Clamp((1 - r) * size / 2 + 0.5f, 0, 1) * 255);
				pixels[y * size + x] = c;
			}
		}
		Tex tex = Tex.FromColors(pixels, size, size);
		tex.AddressMode = TexAddress.Clamp;
		return tex;
	}

	static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

	/// <summary>Paint colors are gamma space bytes, like the paint texture.</summary>
	public static Color Gamma(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);

	/// <summary>For tinting, which takes linear colors.</summary>
	public static Color Linear(Color32 c) => Gamma(c).ToLinear();
}
