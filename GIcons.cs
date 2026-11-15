using System;
using System.Collections.Generic;
using StereoKit;

namespace gesh;

enum GIcon
{
	// Tools.
	Edit,
	Select,
	Delete,
	Brush,
	Fill,
	Pipette,
	// Actions.
	Save,
	Add,
	Close,
	Home,
	Files,
	Settings,
	Info,
	ResetView,
	Undo,
	Redo,
	// Gesture hints.
	Hand,
	Grab,
	Extend,
	Move,
	// Plain shapes for drawing custom UI elements, see GDraw.
	Square,
	Disc,
	Ring,
}

/// <summary>
/// The menu's icons: Google's Material Symbols (Rounded, filled, Apache 2.0),
/// rasterized to white PNGs in Assets/icons by scripts/icons.py, so the UI
/// can tint them. The two pinch icons are a recorded hand instead, rendered
/// white by scripts/tutorial.py. The plain shapes are drawn here in code (signed distance
/// fields), since they're geometry rather than pictures.
/// </summary>
static class GIcons
{
	public const int SIZE = 128;
	static readonly Dictionary<GIcon, Sprite> sprites = new();
	static readonly Dictionary<GIcon, Tex> textures = new();

	/// <summary>The Material Symbols name of each icon, null for shapes.</summary>
	static string? FileName(GIcon icon) => icon switch
	{
		GIcon.Edit => "edit",
		GIcon.Select => "ink_selection",
		GIcon.Delete => "delete",
		GIcon.Brush => "brush",
		GIcon.Fill => "format_color_fill",
		GIcon.Pipette => "colorize",
		GIcon.Save => "save",
		GIcon.Add => "add",
		GIcon.Close => "close",
		GIcon.Home => "home",
		GIcon.Files => "folder",
		GIcon.Settings => "settings",
		GIcon.Info => "info",
		GIcon.ResetView => "recenter",
		GIcon.Undo => "undo",
		GIcon.Redo => "redo",
		GIcon.Hand => "back_hand",
		// The recorded hand mid pinch, see scripts/tutorial.py.
		GIcon.Grab => "pinch",
		GIcon.Extend => "pinch_middle",
		GIcon.Move => "open_with",
		GIcon.Square or GIcon.Disc or GIcon.Ring => null,
		_ => throw new ArgumentException($"No icon for {icon}"),
	};

	/// <summary>Loads every icon and waits for them, so the menu never
	/// shows a placeholder while one loads. Call once after SK.Initialize.</summary>
	public static void Preload()
	{
		foreach (GIcon icon in Enum.GetValues<GIcon>()) Texture(icon);
		Assets.BlockForPriority(int.MaxValue);
	}

	public static Sprite Get(GIcon icon)
	{
		if (!sprites.TryGetValue(icon, out Sprite? sprite))
		{
			sprite = Sprite.FromTex(Texture(icon));
			sprites[icon] = sprite;
		}
		return sprite;
	}

	public static Tex Texture(GIcon icon)
	{
		if (!textures.TryGetValue(icon, out Tex? tex))
		{
			string? file = FileName(icon);
			tex = file != null ? Tex.FromFile($"icons/{file}.png") : Tex.FromColors(Pixels(icon), SIZE, SIZE);
			tex.AddressMode = TexAddress.Clamp;
			textures[icon] = tex;
		}
		return tex;
	}

	/// <summary>A plain shape's pixels.</summary>
	public static Color32[] Pixels(GIcon shape) => Rasterize(Shape(shape));

	/// <summary>Distance from p to the shape's edge, negative inside. In a
	/// unit square, x right and y down.</summary>
	static Func<Vec2, float> Shape(GIcon shape) => shape switch
	{
		GIcon.Square => p => Box(p, new(0.5f, 0.5f), new(0.46f, 0.46f), 0.14f),
		GIcon.Disc => p => Circle(p, new(0.5f, 0.5f), 0.47f),
		GIcon.Ring => p => MathF.Abs(Circle(p, new(0.5f, 0.5f), 0.42f)) - 0.06f,
		_ => throw new ArgumentException($"{shape} isn't a plain shape"),
	};

	static Color32[] Rasterize(Func<Vec2, float> shape)
	{
		var pixels = new Color32[SIZE * SIZE];
		float pixel = 1f / SIZE;
		for (int y = 0; y < SIZE; y++)
		{
			for (int x = 0; x < SIZE; x++)
			{
				float d = shape(new Vec2((x + 0.5f) * pixel, (y + 0.5f) * pixel));
				// One pixel wide anti-aliased edge.
				float coverage = float.Clamp(0.5f - d / pixel, 0, 1);
				pixels[y * SIZE + x] = new Color32(255, 255, 255, (byte)(coverage * 255));
			}
		}
		return pixels;
	}

	static float Circle(Vec2 p, Vec2 center, float radius) => (p - center).Length - radius;

	/// <summary>A rounded box.</summary>
	static float Box(Vec2 p, Vec2 center, Vec2 halfSize, float round)
	{
		Vec2 d = p - center;
		Vec2 q = new Vec2(MathF.Abs(d.x), MathF.Abs(d.y)) - halfSize + new Vec2(round, round);
		return new Vec2(MathF.Max(q.x, 0), MathF.Max(q.y, 0)).Length + MathF.Min(MathF.Max(q.x, q.y), 0) - round;
	}
}
