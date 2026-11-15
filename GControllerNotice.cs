using System;
using StereoKit;

namespace gesh;

/// <summary>
/// Everything in gesh is done with bare hands, so while the hands come from
/// controllers a notice floats in front of the user saying so, until they
/// put the controllers down and hand tracking takes over. It trails the view
/// rather than being stuck to it.
/// </summary>
class GControllerNotice
{
	const float WIDTH = 0.3f;
	/// <summary>How far in front of the eyes, and how far below them.</summary>
	const float DISTANCE = 0.5f, BELOW = 0.05f;
	/// <summary>How quickly it catches up with the view, in seconds.</summary>
	const float FOLLOW = 0.3f;

	/// <summary>Where it is, while it shows.</summary>
	Pose? pose;

	/// <summary>Whether the hands come from controllers right now. A mouse
	/// on desktop also simulates hands, so a tracked controller is what
	/// tells.</summary>
	static bool UsingControllers()
	{
		foreach (Handed h in new[] { Handed.Left, Handed.Right })
			if (Input.HandSource(h) == HandSource.Simulated && Input.Controller(h).IsTracked) return true;
		return false;
	}

	public void Tick()
	{
		if (!UsingControllers())
		{
			pose = null;
			return;
		}
		Pose head = Input.Head;
		Vec3 forward = head.Forward;
		Vec3 at = head.position + forward * DISTANCE + Vec3.Up * -BELOW;
		Pose target = new(at, Quat.LookAt(at, head.position));
		float follow = 1 - MathF.Exp(-Time.Stepf / FOLLOW);
		pose = pose.HasValue ? Pose.Lerp(pose.Value, target, follow) : target;
		Draw(pose.Value);
	}

	public static void Draw(Pose at)
	{
		UI.WindowBegin("gesh controllers", ref at, new Vec2(WIDTH, 0), UIWin.Body, UIMove.None);
		const float ICON = 0.04f;
		Bounds b = UI.LayoutReserve(new Vec2(UI.LayoutRemaining.x, ICON));
		GDraw.Shape(GIcon.Hand, b.center, new Vec2(ICON, ICON), Color.White);
		UI.Text("This experience is designed for hand tracking", Align.TopCenter, TextFit.Wrap, new Vec2(UI.LayoutRemaining.x, 0));
		UI.PushTextStyle(GTheme.Small);
		UI.Text("Put your controllers down to use your hands.", Align.TopCenter, TextFit.Wrap, new Vec2(UI.LayoutRemaining.x, 0));
		UI.PopTextStyle();
		UI.WindowEnd();
	}
}
