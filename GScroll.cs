using System;
using StereoKit;

namespace gesh;

/// <summary>
/// A list scrolled by rows, two ways: its scroll bar, or dragging the list
/// itself like a page, which keeps gliding after the finger lets go and slows
/// to a stop. Controls in the list still take touches on themselves:
/// StereoKit gives a finger to the closest element, and they stand above the
/// list's surface.
/// </summary>
class GScroll
{
	public const float BAR = 0.02f;
	/// <summary>How fast a fling slows down: its speed falls by e this often.</summary>
	const float FRICTION_SECONDS = 0.35f;
	/// <summary>Rows a second below which a glide stops.</summary>
	const float STOP_SPEED = 0.5f;

	/// <summary>The first row shown, fractional while it moves.</summary>
	float position;
	/// <summary>Rows a second, while gliding or being dragged.</summary>
	float velocity;
	/// <summary>While a finger drags the list: where it touched, as a
	/// fraction of the list's height from the top, and the position then.</summary>
	(float touch, float position)? drag;

	/// <summary>The first row shown, fractional while it moves, for lists
	/// that draw rows part way.</summary>
	public float Position => position;

	/// <summary>The first whole row to show.</summary>
	public int First => (int)MathF.Round(position);

	/// <summary>Jumps to a row, for showing a list from some point.</summary>
	public void ScrollTo(float row)
	{
		position = row;
		velocity = 0;
	}

	/// <summary>Takes this frame's touches. Call before drawing the rows, so
	/// the rows are on top.</summary>
	/// <param name="topLeft">The list's top left corner, without the bar.</param>
	/// <param name="size">The list's size, without the bar, which goes just
	/// right of it.</param>
	/// <param name="rowHeight">A row, gutter included.</param>
	public void Tick(string id, Vec3 topLeft, Vec2 size, float rowHeight, int rows, int shown)
	{
		float max = MathF.Max(0, rows - shown);
		float dt = Time.Stepf;

		Vec2 touch = Vec2.Zero;
		UI.SliderBehavior(topLeft, size, UI.StackHash(id + " drag"), ref touch, Vec2.Zero, Vec2.One, Vec2.Zero, Vec2.Zero, UIConfirm.Push, out UISliderData data);
		if (data.activeState.IsActive())
		{
			drag ??= (touch.y, position);
			// Touch y grows down the list: a finger moving up shows later rows.
			float next = drag.Value.position + (drag.Value.touch - touch.y) * size.y / rowHeight;
			if (dt > 0) velocity = float.Lerp(velocity, (next - position) / dt, 0.5f);
			position = next;
		}
		else
		{
			drag = null;
			position += velocity * dt;
			velocity *= MathF.Exp(-dt / FRICTION_SECONDS);
			if (MathF.Abs(velocity) < STOP_SPEED) velocity = 0;
		}
		if (position < 0 || position > max)
		{
			position = float.Clamp(position, 0, max);
			velocity = 0;
		}

		if (max == 0) return;
		// Layout x grows to the user's left: the bar's top left corner is a
		// gutter past the list's right edge.
		Vec3 barAt = new(topLeft.x - size.x - UI.Settings.gutter, topLeft.y, topLeft.z);
		float bar = position;
		if (UI.VSliderAt(id, ref bar, 0, max, 0, barAt, new Vec2(BAR, size.y)))
		{
			position = bar;
			velocity = 0;
			drag = null;
		}
	}
}
