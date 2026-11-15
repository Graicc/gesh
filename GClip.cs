using System;
using StereoKit;

namespace gesh;

/// <summary>
/// Cuts a scrolled list's contents off at its top and bottom edges, so rows
/// slide in and out of view instead of popping. While pushed, StereoKit's UI
/// elements, sprites and GDraw's shapes are clipped by the renderer
/// (Renderer.PushClip), and GTheme's text styles switch to clipped twins.
/// Rows partly outside still take touches on their hidden part.
/// </summary>
static class GClip
{
	public static bool Active { get; private set; }

	/// <param name="topLeft">The list's top left corner in the current UI
	/// space, as UI.LayoutAt gives it.</param>
	/// <param name="height">The list's height.</param>
	public static void Push(Vec3 topLeft, float height)
	{
		if (Active) throw new InvalidOperationException("GClip doesn't nest");
		Vec3 up = Hierarchy.ToWorldDirection(Vec3.Up).Normalized;
		Vec3 top = Hierarchy.ToWorld(topLeft);
		Vec3 bottom = Hierarchy.ToWorld(topLeft - new Vec3(0, height, 0));
		// Kept where dot(normal, p) + w >= 0: below the top, above the bottom.
		Vec4 topPlane = new(-up.x, -up.y, -up.z, Vec3.Dot(up, top));
		Vec4 bottomPlane = new(up.x, up.y, up.z, -Vec3.Dot(up, bottom));
		Renderer.PushClip(topPlane, bottomPlane);
		GTheme.SetTextClip(topPlane, bottomPlane);
		Active = true;
		UI.PushTextStyle(GTheme.Body);
	}

	public static void Pop()
	{
		if (!Active) throw new InvalidOperationException("No GClip pushed");
		UI.PopTextStyle();
		Active = false;
		Renderer.PopClip();
	}
}
