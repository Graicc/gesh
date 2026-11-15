using System;
using System.Collections.Generic;
using StereoKit;

namespace gesh;

/// <summary>
/// Changing color without turning the palm: while painting, a middle pinch
/// opens a ring of the preset colors around the hand, with the recently
/// used colors in a smaller ring inside it. The inner ring always has a
/// place for each recent color, dim until it's used, so the colors don't
/// move around as the list fills. Moving the hand toward
/// one picks it, releasing applies it. Choosing by direction, not by exact
/// position, so pinch jitter doesn't matter. One per hand. See
/// plans/ui-design.md.
/// </summary>
class GQuickPalette
{
	const float RADIUS = 0.06f;
	const float DISC = 0.022f;
	/// <summary>The recent colors ring, inside the presets.</summary>
	const float RECENT_RADIUS = 0.033f;
	const float RECENT_DISC = 0.018f;
	/// <summary>Within this distance of where the pinch started nothing is
	/// chosen, so releasing without moving changes nothing.</summary>
	const float DEAD_ZONE = 0.02f;

	/// <summary>A color in one of the rings.</summary>
	record struct Slot(bool recent, int index, Color32 color);

	/// <summary>Where the pinch started, while the ring is open.</summary>
	Vec3? center;
	/// <summary>The color the hand points at. Kept from the pinching frames,
	/// because on release the hand's point jumps to the fingertip.</summary>
	Slot? hovered;

	/// <summary>Where the ring is, while it's open.</summary>
	public Vec3? Center => center;
	/// <summary>Which color the hand points at, if any: which ring and where
	/// in it.</summary>
	public (bool recent, int index)? Pointed => hovered is Slot h ? (h.recent, h.index) : null;

	/// <param name="input">This hand's input, in world space.</param>
	/// <param name="allowed">A paint tool is on, and the hand isn't busy with
	/// the menu. Anything else closes the ring without choosing.</param>
	/// <param name="recent">Recently used colors, for the inner ring.</param>
	/// <returns>The chosen color, on the tick it's chosen.</returns>
	public Color32? Tick(GHandInput input, bool allowed, Color32 current, IReadOnlyList<Color32> recent)
	{
		if (!allowed)
		{
			center = null;
			hovered = null;
			return null;
		}
		if (input.state != GHandState.MiddlePinch)
		{
			Color32? chosen = hovered?.color;
			center = null;
			hovered = null;
			return chosen;
		}

		center ??= input.pose.position;
		Vec3 head = Input.Head.position;
		Quat facing = Quat.LookAt(center.Value, head);
		hovered = Hovered(input.pose.position - center.Value, facing, recent);
		Draw(center.Value, facing, current, recent);
		return null;
	}

	/// <summary>Direction of slot i of n as seen by the user: the first at
	/// the top, then clockwise. In the ring's own space, whose x points to the
	/// user's left.</summary>
	static Vec3 Direction(int i, int n)
	{
		float angle = i * MathF.Tau / n;
		return new Vec3(-MathF.Sin(angle), MathF.Cos(angle), 0);
	}

	/// <summary>Choosing by direction, from the ring the hand is closer to.</summary>
	static Slot? Hovered(Vec3 offset, Quat facing, IReadOnlyList<Color32> recent)
	{
		Vec3 local = facing.Inverse * offset;
		Vec2 seen = new(-local.x, local.y);
		if (seen.Length < DEAD_ZONE) return null;
		bool inner = seen.Length < (RECENT_RADIUS + RADIUS) / 2;
		int n = inner ? GSettings.RECENT_COLORS : GPaintTab.PRESETS.Length;
		float angle = (MathF.Atan2(seen.x, seen.y) + MathF.Tau) % MathF.Tau;
		int i = (int)MathF.Round(angle / (MathF.Tau / n)) % n;
		// An empty place picks nothing.
		if (inner && i >= recent.Count) return null;
		return new Slot(inner, i, inner ? recent[i] : GPaintTab.PRESETS[i]);
	}

	void Draw(Vec3 at, Quat facing, Color32 current, IReadOnlyList<Color32> recent)
	{
		GDraw.ShapeAt(GIcon.Disc, Matrix.TRS(at, facing, 0.012f), GPaintTab.Linear(current));
		for (int i = 0; i < GPaintTab.PRESETS.Length; i++) DrawSlot(at, facing, new Slot(false, i, GPaintTab.PRESETS[i]), GPaintTab.PRESETS.Length);
		for (int i = 0; i < GSettings.RECENT_COLORS; i++)
		{
			if (i < recent.Count) DrawSlot(at, facing, new Slot(true, i, recent[i]), GSettings.RECENT_COLORS);
			else GDraw.ShapeAt(GIcon.Disc, Matrix.TRS(at + facing * (Direction(i, GSettings.RECENT_COLORS) * RECENT_RADIUS), facing, RECENT_DISC), new Color(1, 1, 1, 0.15f));
		}
	}

	/// <summary>A color's disc, with an outline while it's hovered.</summary>
	void DrawSlot(Vec3 at, Quat facing, Slot slot, int n)
	{
		float size = slot.recent ? RECENT_DISC : DISC;
		Vec3 pos = at + facing * (Direction(slot.index, n) * (slot.recent ? RECENT_RADIUS : RADIUS));
		GDraw.ShapeAt(GIcon.Disc, Matrix.TRS(pos, facing, size), GPaintTab.Linear(slot.color));
		if (hovered.HasValue && hovered.Value.recent == slot.recent && hovered.Value.index == slot.index)
			GDraw.ShapeAt(GIcon.Ring, Matrix.TRS(pos, facing, size * 1.45f), Color.White, layer: 1);
	}
}
