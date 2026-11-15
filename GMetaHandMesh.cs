using System;
using System.Linq;
using System.Runtime.InteropServices;
using StereoKit;
using StereoKit.Framework;

namespace gesh;

/// <summary>
/// Draws Meta's own hand mesh (XR_FB_hand_tracking_mesh, the hands the
/// system menus show) instead of StereoKit's generated one. The runtime hands
/// over a skinned mesh once per hand, fetched on StereoKit's own hand
/// trackers (the Quest allows one per hand), and each frame it's skinned to
/// the joints StereoKit located. Joints and mesh use OpenXR's joint order
/// throughout, so nothing is mapped. Register with
/// SK.AddStepper before SK.Initialize. See plans/hand-mesh.md.
///
/// The meshes are fetched once the app has focus, not at startup: the Quest
/// refuses hand tracking to an app without VR focus ("HAND_TRACKER: client
/// ... does not have access"), and at startup it doesn't have it yet. Until
/// they load, StereoKit's hands show.
/// </summary>
class GMetaHandMesh : IStepper
{
	const string EXT = "XR_FB_hand_tracking_mesh";
	const int JOINTS = 26;

	/// <summary>Between attempts to fetch the meshes, while they fail.</summary>
	const double RETRY_SECONDS = 1;

	/// <summary>Both hands, once their meshes have loaded. Both or neither.</summary>
	Hand[]? hands;
	double nextAttempt;
	/// <summary>The last failure, logged once rather than every retry.</summary>
	string? lastError;
	/// <summary>The hand's dark fill and the light outline shell around
	/// it, see hand.hlsl. Made in Initialize: StereoKit's assets don't exist
	/// before it.</summary>
	Material depth = null!, fill = null!, outline = null!;
	/// <summary>How much bigger than the tracked hand the fill is drawn, and
	/// the outline's width past that, both pushed out along the normals.</summary>
	const float GROW = 0.0015f, OUTLINE = 0.002f;

	/// <summary>The headset offers the mesh.</summary>
	public bool Available { get; private set; }

	/// <summary>Draw Meta's hands instead of StereoKit's, where Available.</summary>
	public bool Enabled { get; set; }

	/// <summary>The hands' colors by tool, read each frame.</summary>
	public GHandColors? Colors { get; set; }

	/// <summary>One hand's skinned mesh. Its tracker is StereoKit's own: the
	/// Quest allows one tracker per hand, and refuses a second with
	/// XR_ERROR_VALIDATION_FAILURE.</summary>
	record Hand(Handed side, Mesh mesh, int vertices, Matrix[] palette, Pose[] joints);

	public GMetaHandMesh()
	{
		if (SK.IsInitialized) throw new InvalidOperationException("GMetaHandMesh must be registered before SK.Initialize");
		Backend.OpenXR.RequestExt("XR_EXT_hand_tracking");
		Backend.OpenXR.RequestExt(EXT);
	}

	/// <summary>All after the scene, like StereoKit's hands. First the
	/// hand's depth alone, invisibly, so the fill after it draws only the
	/// nearest surface: one layer of opacity however the fingers overlap.
	/// Then the outline shell with only its back faces, so the fill hides
	/// all of it but the band around its edge.</summary>
	static (Material depth, Material fill, Material outline) MakeMaterials()
	{
		Material depth = new("hand.hlsl")
		{
			Transparency = Transparency.Blend,
			FaceCull = Cull.Back,
			DepthWrite = true,
			QueueOffset = 10,
		};
		depth["extrude"] = GROW;
		depth["pass"] = 2f;
		Material fill = depth.Copy();
		fill.DepthWrite = false;
		fill.DepthTest = DepthTest.LessOrEq;
		fill.QueueOffset = 11;
		fill["pass"] = 0f;
		Material outline = depth.Copy();
		outline.FaceCull = Cull.Front;
		outline.QueueOffset = 12;
		outline["pass"] = 1f;
		outline["extrude"] = GROW + OUTLINE;
		return (depth, fill, outline);
	}

	public bool Initialize()
	{
		(depth, fill, outline) = MakeMaterials();
		Available = Backend.XRType == BackendXRType.OpenXR
			&& Backend.OpenXR.ExtEnabled("XR_EXT_hand_tracking")
			&& Backend.OpenXR.ExtEnabled(EXT)
			&& LoadBindings();
		return true;
	}

	public void Step()
	{
		bool wanted = Enabled && Available;
		if (wanted && hands == null && SK.AppFocus == AppFocus.Active && Time.Total >= nextAttempt) TryLoad();
		if (!wanted || hands == null)
		{
			Input.HandVisible(Handed.Max, true);
			return;
		}

		foreach (Hand hand in hands!)
		{
			// The joints as StereoKit located them this frame: calling
			// xrLocateHandJointsEXT from here failed validation on the Quest.
			bool valid = Backend.OpenXR.HandJoints(hand.side, hand.joints);
			// StereoKit's hand shows instead whenever this one can't be drawn.
			Input.HandVisible(hand.side, !valid);
			if (!valid) continue;

			for (int j = 0; j < JOINTS; j++) hand.palette[j] = hand.joints[j].ToMatrix();
			hand.mesh.UpdateSkin(hand.palette);
			if (Colors != null)
				foreach (Material m in new[] { fill, outline })
				{
					m["hand_color"] = Colors.Hand;
					m["index_color"] = Colors.Index;
				}
			// The outline brighter while pinching, like StereoKit's hands.
			Color pinch = Input.Hand(hand.side).IsPinched ? new Color(1.4f, 1.4f, 1.4f) : Color.White;
			hand.mesh.Draw(depth, Matrix.Identity, pinch);
			hand.mesh.Draw(fill, Matrix.Identity, pinch);
			hand.mesh.Draw(outline, Matrix.Identity, pinch);
		}
	}

	/// <summary>The trackers are StereoKit's, so there's nothing to free.</summary>
	public void Shutdown() { }

	/// <summary>Fetches both hands' meshes, or neither, trying again later on
	/// failure.</summary>
	void TryLoad()
	{
		var (left, leftError) = LoadHand(Handed.Left);
		var (right, rightError) = LoadHand(Handed.Right);
		if (left != null && right != null)
		{
			hands = [left, right];
			Log.Info($"Meta hand mesh loaded: {left.vertices} vertices a hand");
			return;
		}
		nextAttempt = Time.Total + RETRY_SECONDS;
		string error = leftError ?? rightError ?? throw new InvalidOperationException("A hand failed without an error");
		if (error != lastError) Log.Warn($"Meta hand mesh not loaded yet, retrying: {error}");
		lastError = error;
	}

	/// <summary>The hand's mesh, or why not.</summary>
	static (Hand? hand, string? error) LoadHand(Handed side)
	{
		ulong tracker = Backend.OpenXR.HandTracker(side);
		if (tracker == 0) return (null, $"StereoKit has no hand tracker for {side}");
		// Once for the sizes, once for the data.
		var sizes = new XrHandTrackingMeshFB();
		XrResult result = xrGetHandMeshFB(tracker, ref sizes);
		if (result != XrResult.Success || sizes.jointCountOutput != JOINTS)
		{
			return (null, $"xrGetHandMeshFB {side}: {result}, {sizes.jointCountOutput} joints");
		}

		var bind = new XrPosef[sizes.jointCountOutput];
		var radii = new float[sizes.jointCountOutput];
		var parents = new int[sizes.jointCountOutput];
		var positions = new Vec3[sizes.vertexCountOutput];
		var normals = new Vec3[sizes.vertexCountOutput];
		var uvs = new Vec2[sizes.vertexCountOutput];
		var blendIds = new short[sizes.vertexCountOutput * 4];
		var weights = new Vec4[sizes.vertexCountOutput];
		var indices = new short[sizes.indexCountOutput];
		var pins = new[] { bind, radii, parents, positions, normals, uvs, blendIds, weights, (object)indices }
			.Select(a => GCHandle.Alloc(a, GCHandleType.Pinned)).ToArray();
		try
		{
			var data = new XrHandTrackingMeshFB
			{
				jointCapacityInput = sizes.jointCountOutput,
				jointBindPoses = pins[0].AddrOfPinnedObject(),
				jointRadii = pins[1].AddrOfPinnedObject(),
				jointParents = pins[2].AddrOfPinnedObject(),
				vertexCapacityInput = sizes.vertexCountOutput,
				vertexPositions = pins[3].AddrOfPinnedObject(),
				vertexNormals = pins[4].AddrOfPinnedObject(),
				vertexUVs = pins[5].AddrOfPinnedObject(),
				vertexBlendIndices = pins[6].AddrOfPinnedObject(),
				vertexBlendWeights = pins[7].AddrOfPinnedObject(),
				indexCapacityInput = sizes.indexCountOutput,
				indices = pins[8].AddrOfPinnedObject(),
			};
			result = xrGetHandMeshFB(tracker, ref data);
		}
		finally { foreach (GCHandle pin in pins) pin.Free(); }
		if (result != XrResult.Success)
		{
			return (null, $"xrGetHandMeshFB {side}: {result}");
		}
		// Meta's own texture coordinates go unused. Instead, see hand.hlsl:
		// u is how much the index finger moves the vertex, and v how near
		// the wrist it is.
		// Nearness to the wrist is measured along the hand, toward the palm,
		// so everything behind the wrist (the forearm stub, which was showing
		// grey under the wrist) counts as at the wrist and is hidden.
		Vec3 wrist = bind[WRIST].position;
		Vec3 along = (bind[PALM].position - wrist).Normalized;
		var verts = new Vertex[positions.Length];
		for (int i = 0; i < verts.Length; i++)
		{
			float index = 0;
			for (int k = 0; k < 4; k++)
				if (blendIds[i * 4 + k] is >= INDEX_FIRST and <= INDEX_LAST) index += k switch { 0 => weights[i].x, 1 => weights[i].y, 2 => weights[i].z, _ => weights[i].w };
			float nearWrist = float.Clamp(1 - Vec3.Dot(positions[i] - wrist, along) / WRIST_FADE, 0, 1);
			verts[i] = new Vertex(positions[i], normals[i], new Vec2(index, nearWrist));
		}
		var mesh = new Mesh();
		mesh.SetData(verts, Array.ConvertAll(indices, i => (uint)(ushort)i));
		mesh.SetSkin(Array.ConvertAll(blendIds, i => (ushort)i), weights, Array.ConvertAll(bind, p => p.Matrix));
		return (new Hand(side, mesh, verts.Length, new Matrix[JOINTS], new Pose[JOINTS]), null);
	}

	/// <summary>OpenXR's index finger joints outside the palm: proximal to
	/// tip. The metacarpal is inside the palm, so it doesn't take the index
	/// finger's color.</summary>
	const int INDEX_FIRST = 7, INDEX_LAST = 10;
	const int PALM = 0, WRIST = 1;
	/// <summary>Sets where the hand fades out toward the wrist, see
	/// hand.hlsl: it's gone until 0.55 of this past the wrist joint toward
	/// the palm and solid past 0.7 of it, so 3 cm and 3.9 cm.</summary>
	const float WRIST_FADE = 0.055f;

	#region OpenXR bindings

	/// <summary>Only success is named; failures log as their numbers.</summary>
	enum XrResult : int { Success = 0 }
	enum XrStructureType : uint
	{
		HandTrackingMeshFB = 1000110001,
	}

	[StructLayout(LayoutKind.Sequential)]
	struct XrPosef
	{
		public Quat orientation;
		public Vec3 position;
		public readonly Matrix Matrix => Matrix.TR(position, orientation);
	}

	[StructLayout(LayoutKind.Sequential)]
	struct XrHandTrackingMeshFB
	{
		XrStructureType type;
		IntPtr next;
		public uint jointCapacityInput;
		public uint jointCountOutput;
		public IntPtr jointBindPoses;
		public IntPtr jointRadii;
		public IntPtr jointParents;
		public uint vertexCapacityInput;
		public uint vertexCountOutput;
		public IntPtr vertexPositions;
		public IntPtr vertexNormals;
		public IntPtr vertexUVs;
		public IntPtr vertexBlendIndices;
		public IntPtr vertexBlendWeights;
		public uint indexCapacityInput;
		public uint indexCountOutput;
		public IntPtr indices;

		public XrHandTrackingMeshFB() { type = XrStructureType.HandTrackingMeshFB; }
	}

	delegate XrResult del_xrGetHandMeshFB(ulong handTracker, ref XrHandTrackingMeshFB mesh);

	static del_xrGetHandMeshFB xrGetHandMeshFB = null!;

	static bool LoadBindings()
	{
		xrGetHandMeshFB = Backend.OpenXR.GetFunction<del_xrGetHandMeshFB>("xrGetHandMeshFB");
		return xrGetHandMeshFB != null;
	}

	#endregion
}
