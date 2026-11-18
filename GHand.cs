using System;
using System.Collections.Generic;
using System.Linq;
using StereoKit;

namespace gesh;

enum GHandState
{
	Open,
	IndexPinch,
	MiddlePinch,
	/// <summary>A pinch that started on the menu. It's ignored until the
	/// fingers open again.</summary>
	UIPinch,
}

/// <param name="closest">Closest the smoothed thumb to finger distance got
/// during the current pinch, for the experimental release mode.</param>
/// <param name="smoothed">The pinching finger's thumb distance, low pass
/// filtered, so a one frame tracking spike neither sets `closest` too low nor
/// reads as the fingers opening.</param>
/// <param name="time">When this input was read, in seconds.</param>
/// <param name="grip">Where the pinch started, relative to the palm. A pinch
/// follows the palm from there, not the fingertips.</param>
record struct GHandInput(GHandState state, Pose pose, float closest = 0, float smoothed = 0, double time = 0, Vec3 grip = default)
{
	/// <summary>Time constant of the pinch distance filter.</summary>
	const float SMOOTHING_SECONDS = 0.04f;

	/// <param name="touchingUI">The hand is touching the menu, so new pinches
	/// belong to the menu. Pinches that are already going keep going.</param>
	public static GHandInput GetInput(Handed handed, GHandInput last, GSettings settings, bool touchingUI)
	{
		Hand hand = Input.Hand(handed);
		float indexDist = Gap(hand, FingerId.Index);
		float middleDist = Gap(hand, FingerId.Middle);
		float blend = 1 - MathF.Exp(-Time.Stepf / SMOOTHING_SECONDS);

		// A pinch starts on the raw distance, so it starts without delay, and
		// carries on while the smoothed distance stays closed.
		(GHandState state, float closest, float smoothed) Continue(GHandState pinch, float distance)
		{
			float smoothed = float.Lerp(last.smoothed, distance, blend);
			return StillPinching(smoothed, last.closest, settings) ? (pinch, float.Min(smoothed, last.closest), smoothed) : (GHandState.Open, 0, 0);
		}

		float either = float.Min(indexDist, middleDist);
		(GHandState state, float closest, float smoothed) = last.state switch
		{
			GHandState.Open when touchingUI && either < settings.pinchStartGap => (GHandState.UIPinch, either, either),
			GHandState.Open when indexDist < settings.pinchStartGap => (GHandState.IndexPinch, indexDist, indexDist),
			GHandState.Open when middleDist < settings.pinchStartGap => (GHandState.MiddlePinch, middleDist, middleDist),
			GHandState.Open => (GHandState.Open, 0, 0),
			GHandState.IndexPinch => Continue(GHandState.IndexPinch, indexDist),
			GHandState.MiddlePinch => Continue(GHandState.MiddlePinch, middleDist),
			GHandState.UIPinch => Continue(GHandState.UIPinch, either),
			_ => throw new System.NotImplementedException($"{last.state}"),
		};

		Pose indexPose = hand[FingerId.Index, JointId.Tip].Pose;
		Pose middlePose = hand[FingerId.Middle, JointId.Tip].Pose;
		Pose thumbPose = hand[FingerId.Thumb, JointId.Tip].Pose;
		Pose palm = hand.palm;
		// A pinch moves and turns with the palm, not the fingertips: the
		// fingertips move and rotate as the fingers open, which flicked what was
		// held on release. It starts between the fingertips, and stays there
		// relative to the palm.
		Vec3? start = state == last.state ? null : state switch
		{
			GHandState.IndexPinch => Vec3.Lerp(indexPose.position, thumbPose.position, 0.5f),
			GHandState.MiddlePinch => Vec3.Lerp(middlePose.position, thumbPose.position, 0.5f),
			_ => null,
		};
		Vec3 grip = start.HasValue ? palm.orientation.Inverse * (start.Value - palm.position) : last.grip;
		Pose pose = state switch
		{
			GHandState.Open => OpenAims(indexDist, middleDist, settings.tool) == FingerId.Middle ? middlePose : indexPose,
			GHandState.UIPinch => indexPose,
			GHandState.IndexPinch or GHandState.MiddlePinch => new(palm.position + palm.orientation * grip, palm.orientation),
			_ => throw new System.NotImplementedException(),
		};
		return new GHandInput(state, pose, closest, smoothed, Time.Total, grip);
	}

	/// <summary>Within this thumb gap, a finger counts as closing to pinch.</summary>
	const float CLOSING_GAP = 0.03f;

	/// <summary>The gap between the skin of the thumb tip and the finger's
	/// tip: the joints' distance minus their tracked radii, as StereoKit's own
	/// pinch measures it.</summary>
	static float Gap(Hand hand, FingerId finger)
	{
		HandJoint thumb = hand[FingerId.Thumb, JointId.Tip], tip = hand[finger, JointId.Tip];
		return (tip.position - thumb.position).Length - thumb.radius - tip.radius;
	}

	/// <summary>Which fingertip an open hand aims with: the index, unless
	/// editing and the middle finger is closing on the thumb ahead of it, so
	/// what's highlighted is what the middle pinch will extend. Other tools
	/// keep the index: the brush paints from it while the middle pinch opens
	/// the quick palette.</summary>
	static FingerId OpenAims(float indexGap, float middleGap, GTool tool) =>
		tool == GTool.Edit && middleGap < CLOSING_GAP && middleGap < indexGap ? FingerId.Middle : FingerId.Index;

	static bool StillPinching(float distance, float closest, GSettings settings) => settings.experimentalPinch
		? distance <= closest + settings.pinchReleaseDelta
		: distance <= settings.pinchEndGap;

	public GHandInput Transformed(Matrix transform) => this with { pose = transform.Transform(pose) };

	public bool JustIndexPinched(GHandInput last)
	{
		return state == GHandState.IndexPinch && last.state != GHandState.IndexPinch;
	}

	public bool JustMiddlePinched(GHandInput last)
	{
		return state == GHandState.MiddlePinch && last.state != GHandState.MiddlePinch;
	}
}

/// <summary>The paint tool settings for one tick, with the brush radius in
/// mesh space.</summary>
/// <param name="active">False for a hand that's busy with the menu: it keeps
/// the tool but doesn't use it.</param>
/// <param name="radius">The brush's radius, or for Fill how close the
/// fingertip must come to touch a face.</param>
/// <param name="picking">The pipette is armed instead of the tool; radius
/// is then how close the fingertip picks from.</param>
record struct GPaintTool(GTool tool, float radius, Color32 color, bool picking, bool active = true)
{
	public static readonly GPaintTool Edit = new(GTool.Edit, 0, default, false);
}

/// <summary>What a hand did to the mesh in one tick, where it happened, in
/// mesh space. For sounds: the hand itself stays silent, see GSounds.</summary>
record struct GHandEvents(Vec3? grabbed = null, Vec3? released = null, Vec3? merged = null, Vec3? extended = null, Vec3? deleted = null, Vec3? filled = null, Vec3? picked = null);

/// <summary>Grabs, edits and paints the mesh. Works entirely in the mesh's own
/// coordinate space, the caller transforms input into it.</summary>
class GHand(Handed handed)
{
	/// <summary>A double tap's two pinches start at most this far apart in
	/// time. Two middle taps in the air make a new vertex, two index taps go
	/// back to Edit.</summary>
	const double DOUBLE_TAP_SECONDS = 0.4;
	/// <summary>A pinch only counts as a tap if it's let go this quickly...</summary>
	const double TAP_SECONDS = 0.3;
	/// <summary>...and the hand moved less than this, in meters in the room:
	/// in mesh space a hand dragging the world hardly moves.</summary>
	const float TAP_MOVE = 0.03f;

	GMeshPart? held;
	/// <summary>The held part's vertices when it was grabbed. If the part no
	/// longer has these (the other hand removed it, and its index may have been
	/// reused), it's let go.</summary>
	int[] heldPartVertices = [];
	/// <summary>The vertices that move with the hand: the held part's, or the
	/// whole selection when the part is selected.</summary>
	int[] heldVertices = [];
	/// <summary>Each held vertex's offset from the hand when grabbed, in the
	/// hand's own rotation.</summary>
	Vec3[] heldOffsets = [];

	/// <summary>A face made by pulling out an edge, kept facing the viewer
	/// until it's released.</summary>
	GMeshPart? pulledFace;
	int[] pulledFaceVertices = [];

	/// <summary>Where the brush was last frame, so strokes are continuous
	/// even when the hand moves more than the brush size between frames.</summary>
	Vec3? lastBrush;
	/// <summary>The current pinch selected or deleted a part, so it's
	/// theirs, not an air pinch.</summary>
	bool toolPinch;
	/// <summary>The face the fingertip filled last, so it isn't filled again
	/// every frame (a fill is up to a megabyte, and re-uploads the page).</summary>
	int? lastFilled;
	/// <summary>Whether the brush touched the mesh this tick.</summary>
	bool brushTouching;
	/// <summary>The pipette just picked a color with this fingertip, so the
	/// brush or bucket waits until it leaves the mesh: otherwise the same
	/// touch would paint over the spot it sampled.</summary>
	bool pickedTouch;
	/// <summary>While a select pinch sweeps: true if it adds to the selection,
	/// false if it removes (it started on a selected part).</summary>
	bool? selectAdding;
	/// <summary>While a select pinch on the model goes on: when it started,
	/// and the selection before it changed anything. If the other hand also
	/// pinches the model, the two are a grab, and the selection goes back.</summary>
	(double time, int[] selection)? selectPinch;
	/// <summary>This select pinch is half of a two-hand grab of the
	/// selection, see GTwoHandGrab. It doesn't sweep.</summary>
	bool selectGrab;
	/// <summary>This hand holds a part together with the other hand, see
	/// GTwoHandGrab, which moves the vertices instead.</summary>
	bool twoHandGrab;
	/// <summary>The pinch going on, to tell when it ends whether it was a
	/// tap: quick, still, and it did nothing (grabbed, extended, selected,
	/// deleted or picked nothing). `at` is in the room, not mesh space.</summary>
	(GHandState finger, double time, Vec3 at, float moved, bool acted)? pinch;
	/// <summary>The last pinch, if it was a tap: the first half of a double
	/// tap. Any pinch after it uses it up, so a tap with the other finger in
	/// between, or a pinch that did something, starts over.</summary>
	(GHandState finger, double time, Vec3 at)? lastTap;

	GHandInput lastInput = new();

	/// <summary>The current pinch did something to the mesh, so it shouldn't
	/// also move the world.</summary>
	public bool PinchUsed => held.HasValue || toolPinch;

	/// <summary>The hand is in the middle of changing the document: holding a
	/// part, painting, or pinching with a tool. Undo makes a step each time
	/// both hands stop.</summary>
	public bool Acting => held.HasValue || toolPinch || brushTouching || lastFilled.HasValue;

	/// <summary>The brush or bucket is putting paint on the mesh.</summary>
	public bool Painting => brushTouching || lastFilled.HasValue;

	/// <summary>A color the pipette picked up this tick.</summary>
	public Color32? Picked { get; private set; }

	/// <summary>What happened this tick.</summary>
	public GHandEvents Events { get; private set; }

	/// <summary>The brush (not the bucket) is touching the mesh.</summary>
	public bool Brushing => brushTouching;

	/// <summary>The select pinch on the model going on, see selectPinch.</summary>
	public (double time, int[] selection)? SelectPinch => selectPinch;
	public bool SelectGrabbing => selectGrab;

	/// <summary>The vertices this hand is holding, none if it isn't.</summary>
	public int[] HeldVertices => heldVertices;
	public bool TwoHandGrabbing => twoHandGrab;

	/// <summary>Holds this hand's part together with the other hand: it
	/// stops moving the vertices itself until it lets go.</summary>
	public void JoinTwoHandGrab()
	{
		if (!held.HasValue) throw new InvalidOperationException("Joining a two-hand grab without holding a part");
		twoHandGrab = true;
	}

	/// <summary>The other hand let go: this one carries on alone, taking its
	/// part again from where it is now, so nothing jumps.</summary>
	public void LeaveTwoHandGrab(GEditableMesh mesh)
	{
		if (!twoHandGrab) return;
		twoHandGrab = false;
		if (held.HasValue) Grab(mesh, held.Value, lastInput.pose);
	}

	/// <summary>Makes this select pinch half of a two-hand grab: it stops
	/// sweeping until it's released.</summary>
	public void JoinSelectGrab()
	{
		if (!selectPinch.HasValue) throw new InvalidOperationException("Joining a grab without a select pinch on the model");
		selectAdding = null;
		selectGrab = true;
	}

	/// <summary>The air was double pinched this tick, with any tool: back to
	/// Edit.</summary>
	public bool AirDoubleTapped { get; private set; }

	/// <summary>Tracks pinches as taps. Returns whether a pinch starting this
	/// tick is the second tap of a double tap with the same finger.</summary>
	bool TrackTaps(GHandInput input)
	{
		Vec3 at = Input.Hand(handed).palm.position;
		if (pinch is { } p)
		{
			if (input.state == p.finger) pinch = p with { moved = float.Max(p.moved, (at - p.at).Length) };
			else
			{
				bool tap = !p.acted && input.time - p.time < TAP_SECONDS && p.moved < TAP_MOVE;
				lastTap = tap ? (p.finger, p.time, p.at) : null;
				pinch = null;
			}
		}

		if (input.state == GHandState.Open || input.state == lastInput.state) return false;
		bool doubleTap = lastTap is { } t && t.finger == input.state && input.time - t.time < DOUBLE_TAP_SECONDS && (at - t.at).Length < TAP_MOVE;
		lastTap = null;
		// The second tap did something, so it can't start another double tap.
		pinch = (input.state, input.time, at, 0, doubleTap);
		return doubleTap;
	}

	public GHandInput ReadInput(GSettings settings, bool touchingUI) => GHandInput.GetInput(handed, lastInput, settings, touchingUI);

	/// <summary>Is the part still the one with these vertices?</summary>
	static bool StillThere(GEditableMesh mesh, GMeshPart part, int[] vertices)
	{
		int[]? now = mesh.PartVertices(part);
		return now != null && now.Length == vertices.Length && vertices.All(now.Contains);
	}

	/// <summary>Grabs a part. A selected part brings the whole selection.</summary>
	void Grab(GEditableMesh mesh, GMeshPart? part, Pose pose)
	{
		held = part;
		heldPartVertices = part.HasValue ? mesh.PartVertices(part.Value) ?? throw new InvalidOperationException($"Grabbing removed {part}") : [];
		heldVertices = part.HasValue && mesh.IsSelected(part.Value) ? [.. mesh.selected] : heldPartVertices;
		Quat toHand = pose.orientation.Inverse;
		heldOffsets = heldVertices.Select(v => toHand * (mesh.vertices.values[v].pos - pose.position)).ToArray();
	}

	void Release()
	{
		held = null;
		heldPartVertices = [];
		heldVertices = [];
		heldOffsets = [];
		twoHandGrab = false;
		pulledFace = null;
		pulledFaceVertices = [];
	}

	/// <param name="input">Hand input, in mesh space.</param>
	/// <param name="grabRadius">How close to grab things from, in mesh space.</param>
	/// <param name="viewer">The user's head position, in mesh space.</param>
	/// <param name="paint">The tool in use. Only Edit grabs and extends.</param>
	public void Tick(GEditableMesh mesh, GHandInput input, float grabRadius, Vec3 viewer, GPaintTool paint)
	{
		var pose = input.pose;
		Picked = null;
		AirDoubleTapped = false;
		GHandEvents events = new();
		bool picking = paint.picking && paint.active;
		if (input.state != GHandState.IndexPinch) toolPinch = false;
		bool doubleTap = TrackTaps(input);

		if (held.HasValue && !(StillThere(mesh, held.Value, heldPartVertices) && heldVertices.All(mesh.vertices.IsAlive))) Release();
		if (pulledFace.HasValue && !StillThere(mesh, pulledFace.Value, pulledFaceVertices))
		{
			pulledFace = null;
			pulledFaceVertices = [];
		}

		bool editing = paint.tool == GTool.Edit && paint.active;
		if (input.JustIndexPinched(lastInput) && editing)
		{
			Grab(mesh, mesh.GetClosestPart(pose.position, grabRadius), pose);
			if (held.HasValue) events = events with { grabbed = pose.position };
		}

		if (pickedTouch && (input.state != GHandState.Open || !mesh.GetClosestFace(pose.position, paint.radius).HasValue)) pickedTouch = false;

		// The pipette picks the color the open hand's fingertip touches, no
		// pinch needed, like the brush and the bucket.
		if (picking && input.state == GHandState.Open)
		{
			Picked = mesh.ColorAt(pose.position, paint.radius);
			if (Picked.HasValue)
			{
				events = events with { picked = pose.position };
				pickedTouch = true;
			}
		}

		// The paint bucket fills each face the open hand's fingertip touches,
		// like finger painting.
		if (paint.tool == GTool.Fill && paint.active && !picking && !pickedTouch && input.state == GHandState.Open)
		{
			GMeshPart? face = mesh.GetClosestFace(pose.position, paint.radius);
			if (face.HasValue && face.Value.Index != lastFilled)
			{
				mesh.FillFace(face.Value.Index, paint.color);
				events = events with { filled = pose.position };
			}
			lastFilled = face?.Index;
		}
		else
		{
			lastFilled = null;
		}

		// The select tool sweeps parts into the selection, or out of it if the
		// pinch started on a selected part. Pinching air still moves the world.
		if (paint.tool == GTool.Select && paint.active && input.state == GHandState.IndexPinch)
		{
			GMeshPart? part = mesh.GetClosestPart(pose.position, grabRadius);
			if (input.JustIndexPinched(lastInput))
			{
				selectAdding = part.HasValue ? !mesh.IsSelected(part.Value) : null;
				selectPinch = part.HasValue ? (input.time, [.. mesh.selected]) : null;
				toolPinch = part.HasValue;
			}
			if (selectAdding.HasValue && part.HasValue)
			{
				foreach (int v in mesh.PartVertices(part.Value)!)
				{
					if (selectAdding.Value) mesh.selected.Add(v);
					else mesh.selected.Remove(v);
				}
			}
		}
		else
		{
			selectAdding = null;
			selectPinch = null;
			selectGrab = false;
		}

		// The delete tool removes the pinched part. Pinching air still moves
		// the world.
		if (paint.tool == GTool.Delete && paint.active && input.JustIndexPinched(lastInput))
		{
			GMeshPart? part = mesh.GetClosestPart(pose.position, grabRadius);
			if (part.HasValue)
			{
				mesh.RemovePart(part.Value);
				events = events with { deleted = pose.position };
			}
			toolPinch = part.HasValue;
		}

		// Two quick index pinches in empty air, with any tool, go back to Edit.
		// A pinch that grabbed, selected, deleted or picked something isn't in
		// the air.
		AirDoubleTapped = paint.active && doubleTap && input.state == GHandState.IndexPinch && !held.HasValue && !toolPinch;

		// The brush paints with the index fingertip whenever the hand is open.
		if (paint.tool == GTool.Brush && paint.active && !picking && !pickedTouch && input.state == GHandState.Open)
		{
			brushTouching = mesh.PaintStroke(lastBrush ?? pose.position, pose.position, paint.radius, paint.color);
			lastBrush = pose.position;
		}
		else
		{
			lastBrush = null;
			brushTouching = false;
		}

		if (input.state == GHandState.Open && held.HasValue)
		{
			// Each held vertex merges into a vertex it was dropped on. Look where
			// the user sees the vertex, not at the hand: opening a pinch moves the
			// hand's point to the index tip, centimeters away from the pinch.
			// Held vertices (a whole selection, maybe) don't merge into each other.
			List<int> moved = [.. heldVertices];
			Vec3? merged = null;
			foreach (int v in heldVertices)
			{
				int? target = mesh.GetClosestVertex(mesh.vertices.values[v].pos, grabRadius, heldVertices);
				if (!target.HasValue) continue;
				merged ??= mesh.vertices.values[target.Value].pos;
				mesh.MergeVertex(v, target.Value);
				moved.Add(target.Value);
			}
			events = merged.HasValue ? events with { merged = merged } : events with { released = pose.position };
			// Faces that changed size get paint cells to match, now that the
			// drag is over.
			mesh.RefitPaintAround(moved);

			Release();
		}

		for (int i = 0; i < heldVertices.Length && !twoHandGrab; i++)
		{
			int v = heldVertices[i];
			mesh.MoveVertex(v, pose.orientation * heldOffsets[i] + pose.position);
		}

		if (pulledFace.HasValue)
		{
			mesh.OrientFaceToward(pulledFace.Value.Index, viewer);
		}

		if (input.JustMiddlePinched(lastInput) && editing)
		{
			var part = mesh.GetClosestPart(pose.position, grabRadius);
			if (!part.HasValue)
			{
				// A double tap in empty air makes a lone vertex, held, which is
				// how an empty scene gets started.
				if (doubleTap)
				{
					int v = mesh.AddVertex(new Vertex(pose.position, Vec3.Up));
					Grab(mesh, new GMeshPart(GMeshPartType.Vertex, v), pose);
					events = events with { extended = pose.position };
				}
			}
			else
			{
				var (newV, newFace) = mesh.Extend(part.Value, pose.position);
				Grab(mesh, newV, pose);
				// One pop for the new vertex, and for the face it pulls out with it.
				events = events with { extended = pose.position };
				if (newFace.HasValue)
				{
					pulledFace = new GMeshPart(GMeshPartType.Face, newFace.Value);
					pulledFaceVertices = mesh.PartVertices(pulledFace.Value)!;
				}
			}
		}

		// A pinch that grabbed, extended, selected, deleted or picked
		// something isn't a tap.
		if (pinch is { } going && (held.HasValue || toolPinch)) pinch = going with { acted = true };

		lastInput = input;
		Events = events;
	}

	public void DrawUI()
	{
		UI.Text($"Hand {handed}");
		UI.Text($"State: {lastInput.state}, Held: {held}");
	}
}
