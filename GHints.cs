using System.Linq;
using StereoKit;

namespace gesh;

/// <summary>
/// First launch: one hint at a time above the model, each gone once its
/// gesture is done. Only shown when there were no saved files at launch, so
/// there's nothing to remember: the saved files are the memory. See
/// plans/ui-design.md.
/// </summary>
/// <param name="start">The starting document, to see the first edits.</param>
class GHints(bool enabled, GDocument start)
{
	int startHash = start.mesh.ContentHash();
	int startFaces = start.mesh.faces.Indices().Count();

	/// <summary>Each step's gesture, shown played by a recorded hand above the
	/// text: Assets/tutorial, from scripts/tutorial.py, with its frame count.</summary>
	static readonly (GIcon icon, string text, string animation, int frames)[] STEPS =
	[
		(GIcon.Grab, "Pinch a corner to grab it", "tutorial/pinch.png", 75),
		(GIcon.Extend, "Pinch an edge with middle finger to pull out a face", "tutorial/middle.png", 66),
		(GIcon.Hand, "Look at your palm to open the menu", "tutorial/palm.png", 79),
	];
	/// <summary>The animation's size in the hint, in meters.</summary>
	const float ANIMATION = 0.14f;

	int step = enabled ? 0 : STEPS.Length;
	/// <summary>Every step's animation, loaded while the hints show, so the
	/// next step's is ready when it comes.</summary>
	GFlipbook[]? animations = enabled ? LoadAnimations() : null;
	/// <summary>When the current step started, so its animation plays from
	/// the beginning.</summary>
	double stepStart = Time.Total;

	static GFlipbook[] LoadAnimations() => STEPS.Select(s => new GFlipbook(s.animation, s.frames)).ToArray();

	/// <summary>Plays the hints again from the first, on the document as it
	/// is now.</summary>
	public void Restart(GDocument doc)
	{
		step = 0;
		stepStart = Time.Total;
		animations ??= LoadAnimations();
		startHash = doc.mesh.ContentHash();
		startFaces = doc.mesh.faces.Indices().Count();
	}

	public void Tick(GDocument doc, bool menuShown)
	{
		if (step < STEPS.Length && Done(step, doc, menuShown)) { step++; stepStart = Time.Total; }
		if (step >= STEPS.Length || !doc.mesh.vertices.Indices().Any()) return;

		// Above the model, facing the user, following it as the world moves.
		Vec3 top = Vec3.Zero;
		Vec3 sum = Vec3.Zero;
		int count = 0;
		foreach (int v in doc.mesh.vertices.Indices())
		{
			Vec3 p = doc.world.Matrix.Transform(doc.mesh.vertices.values[v].pos);
			sum += p;
			top.y = count == 0 ? p.y : float.Max(top.y, p.y);
			count++;
		}
		Vec3 at = new(sum.x / count, top.y + 0.06f + 0.04f + ANIMATION, sum.z / count);
		// Facing straight at the head, tilting up or down as well as turning.
		Pose pose = new(at, Quat.LookAt(at, Input.Head.position));

		UI.WindowBegin("gesh hint", ref pose, new Vec2(0.2f, 0), UIWin.Body, UIMove.None);
		animations![step].Draw(ANIMATION, Time.Total - stepStart);
		GPalmMenu.Legend(STEPS[step].icon, STEPS[step].text);
		UI.WindowEnd();
	}

	bool Done(int s, GDocument doc, bool menuShown) => s switch
	{
		0 => doc.mesh.ContentHash() != startHash,
		1 => doc.mesh.faces.Indices().Count() > startFaces,
		2 => menuShown,
		_ => throw new System.ArgumentOutOfRangeException(nameof(s)),
	};
}
