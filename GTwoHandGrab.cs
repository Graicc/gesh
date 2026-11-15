using System.Collections.Generic;
using System.Linq;
using StereoKit;

namespace gesh;

/// <summary>
/// Both hands holding the same thing move, rotate and scale it around the
/// point between them, the way two hands in the air move the world. Scale
/// sticks at 100%, like zooming. Two ways to get there:
/// - With the select tool, both hands pinching the model grab the selection.
/// - With the edit tool, both hands holding parts that share vertices (a face
///   and the face again, or one of its edges) grab all of those vertices.
///   Two hands on separate parts, like the two ends of an edge, still drag
///   each on its own.
/// Pinching the air is unaffected.
/// </summary>
class GTwoHandGrab
{
	/// <summary>The hands' motion since the grab started, in world space.</summary>
	GWorld motion = GWorld.Identity;
	GWorldNav nav = new();
	/// <summary>Where each grabbed vertex was when the grab started, in world
	/// space. Null when not grabbing.</summary>
	Dictionary<int, Vec3>? start;
	/// <summary>How small this grab may shrink what it holds, so it stays at
	/// least MIN_SIZE across: shrunk much further, vertices land on the same
	/// float position, which the mesh doesn't allow.</summary>
	float minScale;
	/// <summary>The smallest a grab can shrink things to, across, in mesh
	/// space: zooming in to shrink it further mustn't get around it.</summary>
	const float MIN_SIZE = 0.002f;

	public bool Active => start != null;

	/// <summary>The grabbed vertices' scale relative to when they were grabbed.</summary>
	public float Scale => motion.scale;

	/// <param name="leftAt">The left hand's pinch, in world space.</param>
	/// <param name="rightAt">Same for the right hand.</param>
	public void Tick(GEditableMesh mesh, Matrix toWorld, GHand left, Pose leftAt, GHand right, Pose rightAt, GSettings settings)
	{
		// Both hands pinching the model is a grab, not two sweeps: the
		// selection goes back to how it was before the sweeps started.
		if (left.SelectPinch.HasValue && right.SelectPinch.HasValue && !(left.SelectGrabbing && right.SelectGrabbing))
		{
			var joining = new[] { left, right }.Where(h => !h.SelectGrabbing).MinBy(h => h.SelectPinch!.Value.time)!;
			mesh.selected.Clear();
			mesh.selected.UnionWith(joining.SelectPinch!.Value.selection.Where(mesh.vertices.IsAlive));
			left.JoinSelectGrab();
			right.JoinSelectGrab();
		}

		// Both hands holding the same vertices hold them together.
		if (!(left.TwoHandGrabbing && right.TwoHandGrabbing) && left.HeldVertices.Intersect(right.HeldVertices).Any())
		{
			left.JoinTwoHandGrab();
			right.JoinTwoHandGrab();
		}

		IEnumerable<int>? grabbed =
			left.SelectGrabbing && right.SelectGrabbing ? mesh.selected
			: left.TwoHandGrabbing && right.TwoHandGrabbing ? left.HeldVertices.Union(right.HeldVertices)
			: null;
		if (grabbed == null)
		{
			if (start != null) mesh.RefitPaintAround(start.Keys);
			start = null;
			// A hand still holding takes over on its own.
			left.LeaveTwoHandGrab(mesh);
			right.LeaveTwoHandGrab(mesh);
			return;
		}

		if (start == null)
		{
			start = grabbed.ToDictionary(v => v, v => toWorld.Transform(mesh.vertices.values[v].pos));
			var positions = start.Keys.Select(v => mesh.vertices.values[v].pos).ToList();
			float size = positions.Count == 0 ? 0 : (positions.Aggregate(Vec3.Max) - positions.Aggregate(Vec3.Min)).Length;
			// Already that small, or a single vertex: it doesn't shrink further.
			minScale = size > MIN_SIZE ? float.Max(settings.minWorldScale, MIN_SIZE / size) : 1;
			motion = GWorld.Identity;
			nav = new GWorldNav();
		}
		motion = nav.Tick(motion, leftAt, rightAt, minScale, settings.maxWorldScale, settings.turnSmoothing);
		Matrix toMesh = toWorld.Inverse;
		Matrix moved = motion.Matrix;
		foreach (var (v, at) in start) mesh.MoveVertex(v, toMesh.Transform(moved.Transform(at)));
	}
}
