using System;
using StereoKit;

namespace gesh;

/// <summary>Mesh space to world space. Kept as separate parts instead of a
/// matrix, so changing it every frame can't build up skew or uneven scale.</summary>
record struct GWorld(Vec3 position, Quat rotation, float scale)
{
	public static readonly GWorld Identity = new(Vec3.Zero, Quat.Identity, 1);

	public Matrix Matrix => Matrix.TRS(position, rotation, scale);
}

/// <summary>Moves the world around when the user pinches empty air. One hand
/// holds it like an object: it moves and turns with the hand, around the
/// pinch. Two hands pan, rotate and zoom around the point between them, and
/// turning both palms about the line between them rolls the world about it.</summary>
class GWorldNav
{
	/// <summary>Zooming sticks at 100%: the hands' span can change by this
	/// factor either way before the scale moves off it.</summary>
	const float STICKY_ONE = 1.3f;
	static readonly float STICKY_LOG = MathF.Log(STICKY_ONE);

	// Where each hand was air pinching last frame, null if it wasn't.
	Pose? lastLeft;
	Pose? lastRight;
	/// <summary>While zooming, the scale the hands ask for, before sticking
	/// at 100%. See Sticky.</summary>
	float? handScale;

	/// <summary>The hand scale with the stretch around 1 that sticks at 1
	/// cut out, so scales on either side carry on smoothly from it.</summary>
	static float Sticky(float handScale)
	{
		float log = MathF.Log(handScale);
		return MathF.Abs(log) <= STICKY_LOG ? 1 : MathF.Exp(log - MathF.CopySign(STICKY_LOG, log));
	}

	/// <summary>The inverse of Sticky: the hand scale for a scale, the middle
	/// of the sticky stretch for 1.</summary>
	static float Unsticky(float scale)
	{
		float log = MathF.Log(scale);
		return log == 0 ? 1 : MathF.Exp(log + MathF.CopySign(STICKY_LOG, log));
	}

	/// <summary>The hand's turn eased toward from last frame's, a low pass.
	/// Positions aren't smoothed: they jitter less, and lag would be felt.</summary>
	static Pose? Smoothed(Pose? now, Pose? last, float seconds)
	{
		if (!now.HasValue || !last.HasValue || seconds <= 0) return now;
		float follow = 1 - MathF.Exp(-Time.Stepf / seconds);
		return now.Value with { orientation = Quat.Slerp(last.Value.orientation, now.Value.orientation, follow).Normalized };
	}

	/// <summary>How far a palm turned about the line between the hands,
	/// beyond the swing of the line itself, in radians.</summary>
	/// <param name="axis">The line between the hands last frame, normalized.</param>
	static float Twist(Quat swing, Quat last, Quat now, Vec3 axis)
	{
		// The palm's turn with the swing taken out, around last frame's line.
		Quat rest = (swing.Inverse * now * last.Inverse).Normalized;
		float along = rest.x * axis.x + rest.y * axis.y + rest.z * axis.z;
		float angle = 2 * MathF.Atan2(along, rest.w);
		// The short way around.
		return angle > MathF.PI ? angle - MathF.Tau : angle < -MathF.PI ? angle + MathF.Tau : angle;
	}

	static Quat AxisAngle(Vec3 axis, float radians)
	{
		float s = MathF.Sin(radians / 2);
		return new Quat(axis.x * s, axis.y * s, axis.z * s, MathF.Cos(radians / 2));
	}

	/// <summary>The world held in one hand: it turns with the hand around
	/// the pinch, and moves with it.</summary>
	static GWorld Held(GWorld world, Pose last, Pose now)
	{
		Quat turn = (now.orientation * last.orientation.Inverse).Normalized;
		return world with
		{
			position = turn * (world.position - last.position) + now.position,
			rotation = (turn * world.rotation).Normalized,
		};
	}

	/// <param name="left">Left hand's air pinch irl, turning with the palm,
	/// null if it isn't air pinching.</param>
	/// <param name="right">Same for the right hand.</param>
	/// <returns>The moved world.</returns>
	/// <param name="turnSmoothing">Seconds the hands' turning is smoothed
	/// over, 0 for none: tracking jitter in the palm turns the whole world,
	/// which is uncomfortable without passthrough to steady the eye.</param>
	public GWorld Tick(GWorld world, Pose? left, Pose? right, float minScale, float maxScale, float turnSmoothing = 0)
	{
		left = Smoothed(left, lastLeft, turnSmoothing);
		right = Smoothed(right, lastRight, turnSmoothing);
		GWorld result = world;
		// A new zoom starts from the world's scale.
		if (!(left.HasValue && right.HasValue)) handScale = null;

		if (left.HasValue && right.HasValue && lastLeft.HasValue && lastRight.HasValue)
		{
			Vec3 lastSpan = lastRight.Value.position - lastLeft.Value.position;
			Vec3 span = right.Value.position - left.Value.position;
			// Skip frames where the hands are on top of each other, or swapped
			// sides (tracking glitches), the rotation between them is undefined.
			if (lastSpan.LengthSq > 0.0001f && span.LengthSq > 0.0001f && Vec3.Dot(lastSpan.Normalized, span.Normalized) > -0.9f)
			{
				handScale = float.Clamp((handScale ?? Unsticky(world.scale)) * span.Length / lastSpan.Length, Unsticky(minScale), Unsticky(maxScale));
				float newScale = Sticky(handScale.Value);
				float scaleDelta = newScale / world.scale;
				// The line between the hands swings the world, and the palms
				// turning together about that line rolls it around the line.
				Quat swing = Quat.Delta(lastSpan.Normalized, span.Normalized);
				float roll = (Twist(swing, lastLeft.Value.orientation, left.Value.orientation, lastSpan.Normalized)
					+ Twist(swing, lastRight.Value.orientation, right.Value.orientation, lastSpan.Normalized)) / 2;
				Quat rotationDelta = (swing * AxisAngle(lastSpan.Normalized, roll)).Normalized;

				// Scale and rotate around the old midpoint, then move it to the new one.
				Vec3 lastMid = (lastLeft.Value.position + lastRight.Value.position) / 2;
				Vec3 mid = (left.Value.position + right.Value.position) / 2;
				result = new GWorld(
					rotationDelta * ((world.position - lastMid) * scaleDelta) + mid,
					(rotationDelta * world.rotation).Normalized,
					newScale);
			}
		}
		else if (left.HasValue && lastLeft.HasValue) result = Held(world, lastLeft.Value, left.Value);
		else if (right.HasValue && lastRight.HasValue) result = Held(world, lastRight.Value, right.Value);

		lastLeft = left;
		lastRight = right;
		return result;
	}
}
