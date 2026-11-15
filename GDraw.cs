using System.Collections.Generic;
using StereoKit;

namespace gesh;

/// <summary>
/// Drawing custom visuals inside a UI window, for elements StereoKit's UI
/// doesn't have (color wheel, color swatches, gradient sliders). Positions
/// are in the window's layout space, as UI.LayoutReserve returns them. The
/// window's front is toward -z there, so things are drawn a little in front
/// of the surface.
/// </summary>
static class GDraw
{
	/// <summary>How far in front of the window surface custom visuals sit.</summary>
	public const float LIFT = 0.002f;

	static readonly Dictionary<(Tex, int), Material> materials = new();

	/// <summary>Transparent quads have no reliable order between them, so a
	/// knob's see-through corners could hide the wheel under it. Each layer
	/// gets its own draw queue instead: higher layers draw later, on top.</summary>
	static Material MaterialFor(Tex tex, int layer)
	{
		if (!materials.TryGetValue((tex, layer), out Material? material))
		{
			material = Material.Unlit.Copy();
			material[MatParamName.DiffuseTex] = tex;
			material.Transparency = Transparency.Blend;
			// The see-through corners mustn't hide what's drawn behind them later
			// (the floor, other quads), which shows as a gray square.
			material.DepthWrite = false;
			material.QueueOffset = 1 + layer;
			materials[(tex, layer)] = material;
		}
		return material;
	}

	/// <summary>A texture on a quad, tinted (linear color).</summary>
	/// <param name="layer">Draw order among custom visuals, higher on top.</param>
	/// <param name="lift">Extra distance in front of the surface.</param>
	public static void Texture(Tex tex, Vec3 center, Vec2 size, Color tint, int layer = 0, float lift = 0)
	{
		Vec3 at = center + new Vec3(0, 0, -(LIFT + lift + layer * 0.0005f));
		Mesh.Quad.Draw(MaterialFor(tex, layer), Matrix.TS(at, new Vec3(size.x, size.y, 1)), tint);
	}

	public static void Shape(GIcon shape, Vec3 center, Vec2 size, Color tint, int layer = 0, float lift = 0) => Texture(GIcons.Texture(shape), center, size, tint, layer, lift);

	/// <summary>A shape on a quad anywhere, not in a window's layout.</summary>
	public static void ShapeAt(GIcon shape, Matrix transform, Color tint, int layer = 0) => Mesh.Quad.Draw(MaterialFor(GIcons.Texture(shape), layer), transform, tint);

	/// <summary>Top left corner of a layout bounds, which UI behaviors take.</summary>
	/// <summary>Diameter of every close button.</summary>
	public const float CLOSE = 0.028f;
	/// <summary>Between a close button and what it closes.</summary>
	const float CLOSE_GAP = 0.008f;

	/// <summary>The one close button: round, floating just off the right of
	/// what it closes, its top level with that thing's top. Its own window,
	/// so it never overlaps the panel.</summary>
	/// <param name="topRight">The top right corner of what it closes, facing
	/// like it.</param>
	public static bool Close(string id, Pose topRight)
	{
		// Window layout x grows to the user's left, so their right is -x.
		Pose at = new(topRight.position + topRight.orientation * new Vec3(-(CLOSE_GAP + CLOSE / 2), 0, 0), topRight.orientation);
		UI.WindowBegin(id, ref at, new Vec2(CLOSE, CLOSE), UIWin.Empty, UIMove.None);
		bool closed = UI.ButtonRound("close", GIcons.Get(GIcon.Close), CLOSE);
		UI.WindowEnd();
		return closed;
	}

	/// <summary>The top right corner of a window whose pose is its top center.</summary>
	public static Pose TopRight(Pose window, float width) => new(window.position + window.orientation * new Vec3(-width / 2, 0, 0), window.orientation);

	/// <summary>A slider of the usual height, centered on a taller row, so it
	/// lines up with the buttons beside it.</summary>
	public static void HSlider(string id, ref float value, float min, float max, Bounds row)
	{
		float height = UI.LineHeight;
		Vec3 topLeft = TopLeft(row) - new Vec3(0, (row.dimensions.y - height) / 2, 0);
		UI.HSliderAt(id, ref value, min, max, 0, topLeft, new Vec2(row.dimensions.x, height));
	}

	/// <summary>How far the middle of the head is behind the eyes, where
	/// Input.Head is.</summary>
	const float HEAD_DEPTH = 0.09f;

	/// <summary>The middle of the user's head, the point that stays put
	/// when they turn it.</summary>
	public static Vec3 HeadCenter => Input.Head.position + Input.Head.orientation * new Vec3(0, 0, HEAD_DEPTH);

	public static Vec3 TopLeft(Bounds b) => b.center + new Vec3(b.dimensions.x / 2, b.dimensions.y / 2, 0);
}
