using StereoKit;

namespace gesh;

enum GHandState
{
	Open,
	TwoFingerPinch,
	ThreeFingerPinch
}

record struct GHandInput(GHandState state, Pose pose)
{
	public static GHandInput GetInput(Handed handed, GHandInput last)
	{
		const float DISTANCE_THRESHOLD_SQ_ON = 0.025f * 0.025f;
		const float DISTANCE_THRESHOLD_SQ_OFF = 0.030f * 0.030f;

		Hand hand = Input.Hand(handed);

		float indexDistSq = (hand[FingerId.Index, JointId.Tip].position - hand[FingerId.Thumb, JointId.Tip].position).MagnitudeSq;
		float middleDistSq = (hand[FingerId.Middle, JointId.Tip].position - hand[FingerId.Thumb, JointId.Tip].position).MagnitudeSq;

		GHandState newState = GHandState.Open;
		switch (last.state)
		{
			case GHandState.Open:
				{
					// if (indexDistSq < DISTANCE_THRESHOLD_SQ_ON && middleDistSq < DISTANCE_THRESHOLD_SQ_ON)
					// {
					// 	newState = GHandState.ThreeFingerPinch;
					// }
					// else if (indexDistSq < DISTANCE_THRESHOLD_SQ_ON)
					// {
					// 	newState = GHandState.TwoFingerPinch;
					// }
					if (indexDistSq < DISTANCE_THRESHOLD_SQ_ON)
					{
						newState = GHandState.TwoFingerPinch;
					}
					else if (middleDistSq < DISTANCE_THRESHOLD_SQ_ON)
					{
						newState = GHandState.ThreeFingerPinch;
					}
					break;
				}
			case GHandState.TwoFingerPinch:
				{
					// if (indexDistSq > DISTANCE_THRESHOLD_SQ_OFF)
					// {
					// 	newState = GHandState.Open;
					// }
					// else if (middleDistSq < DISTANCE_THRESHOLD_SQ_ON)
					// {
					// 	newState = GHandState.ThreeFingerPinch;
					// }
					// else
					// {
					// 	newState = GHandState.TwoFingerPinch;
					// }
					if (indexDistSq <= DISTANCE_THRESHOLD_SQ_OFF)
					{
						newState = GHandState.TwoFingerPinch;
					}
					break;
				}
			case GHandState.ThreeFingerPinch:
				{
					// if (indexDistSq <= DISTANCE_THRESHOLD_SQ_OFF && middleDistSq <= DISTANCE_THRESHOLD_SQ_OFF)
					// {
					// 	newState = GHandState.ThreeFingerPinch;
					// }
					if (middleDistSq <= DISTANCE_THRESHOLD_SQ_OFF)
					{
						newState = GHandState.ThreeFingerPinch;
					}
					break;
				}
		}

		Pose pose = newState == GHandState.ThreeFingerPinch ? hand[FingerId.Middle, JointId.Tip].Pose : hand[FingerId.Index, JointId.Tip].Pose;
		return new GHandInput(newState, pose);
	}

	public bool JustTwoFingerPinch(GHandInput last)
	{
		return state == GHandState.TwoFingerPinch && last.state != GHandState.TwoFingerPinch;
	}

	public bool JustThreeFingerPinch(GHandInput last)
	{
		return state == GHandState.ThreeFingerPinch && last.state != GHandState.ThreeFingerPinch;
	}
}

class GHand(Handed handed)
{
	GMeshPart? held;
	Pose heldOffset;
	Vec3 heldOffsetB;
	Vec3 heldOffsetC;

	GHandInput lastInput = new();

	void Grab(GEditableMesh mesh, GMeshPart? newHeld, Pose pose)
	{
		held = newHeld;

		switch (held?.Type)
		{
			case GMeshPartType.Vertex:
				{
					var v = mesh.vertices.values[held.Value.Index];
					heldOffset = new Pose(v.pos - pose.position, pose.orientation.Inverse);
					break;
				}
			case GMeshPartType.Edge:
				{
					var e = mesh.edges.values[held.Value.Index];
					var a = mesh.vertices.values[e.a];
					var b = mesh.vertices.values[e.b];
					heldOffset = new Pose(a.pos - pose.position, pose.orientation.Inverse);
					heldOffsetB = b.pos - pose.position;
					break;
				}
			case GMeshPartType.Face:
				{
					var f = mesh.faces.values[held.Value.Index];
					var a = mesh.vertices.values[f.a];
					var b = mesh.vertices.values[f.b];
					var c = mesh.vertices.values[f.c];
					heldOffset = new Pose(a.pos - pose.position, pose.orientation.Inverse);
					heldOffsetB = b.pos - pose.position;
					heldOffsetC = c.pos - pose.position;
					break;
				}
		}
	}

	public void Tick(GEditableMesh mesh)
	{
		var input = GHandInput.GetInput(handed, lastInput);
		var pose = input.pose;

		if (input.JustTwoFingerPinch(lastInput))
		{
			Grab(mesh, mesh.GetClosestPart(pose.position), pose);
		}
		else if (input.state == GHandState.Open)
		{
			held = null;
		}

		if (held.HasValue)
		{
			switch (held?.Type)
			{
				case GMeshPartType.Vertex:
					{
						var v = mesh.vertices.values[held.Value.Index];
						var pos = pose.orientation.Rotate(heldOffset.orientation.Rotate(heldOffset.position)) + pose.position;
						mesh.vertices.values[held.Value.Index] = new Vertex(pos, v.norm, v.uv, v.col);
						break;
					}
				case GMeshPartType.Edge:
					{
						var e = mesh.edges.values[held.Value.Index];
						var a = mesh.vertices.values[e.a];
						var b = mesh.vertices.values[e.b];

						var aPos = pose.orientation.Rotate(heldOffset.orientation.Rotate(heldOffset.position)) + pose.position;
						var bPos = pose.orientation.Rotate(heldOffset.orientation.Rotate(heldOffsetB)) + pose.position;

						mesh.vertices.values[e.a] = new Vertex(aPos, a.norm, a.uv, a.col);
						mesh.vertices.values[e.b] = new Vertex(bPos, b.norm, b.uv, b.col);
						break;
					}
				case GMeshPartType.Face:
					{
						var f = mesh.faces.values[held.Value.Index];
						var a = mesh.vertices.values[f.a];
						var b = mesh.vertices.values[f.b];
						var c = mesh.vertices.values[f.c];

						var aPos = pose.orientation.Rotate(heldOffset.orientation.Rotate(heldOffset.position)) + pose.position;
						var bPos = pose.orientation.Rotate(heldOffset.orientation.Rotate(heldOffsetB)) + pose.position;
						var cPos = pose.orientation.Rotate(heldOffset.orientation.Rotate(heldOffsetC)) + pose.position;

						mesh.vertices.values[f.a] = new Vertex(aPos, a.norm, a.uv, a.col);
						mesh.vertices.values[f.b] = new Vertex(bPos, b.norm, b.uv, b.col);
						mesh.vertices.values[f.c] = new Vertex(cPos, b.norm, b.uv, b.col);
						break;
					}
			}
		}

		if (input.JustThreeFingerPinch(lastInput))
		{
			var part = mesh.GetClosestPart(pose.position);
			switch (part?.Type)
			{
				case GMeshPartType.Vertex:
					{
						Vertex v = mesh.vertices.values[part.Value.Index];
						Vertex newVertex = new Vertex(pose.position, v.norm, v.uv, v.col);
						int newV = mesh.AddVertex(newVertex);
						mesh.AddEdge(new GEdge(part.Value.Index, newV));
						Grab(mesh, new GMeshPart(GMeshPartType.Vertex, newV), pose);
						break;
					}
				case GMeshPartType.Edge:
					{
						GEdge e = mesh.edges.values[part.Value.Index];
						Vertex va = mesh.vertices.values[e.a];
						Vertex newVertex = new Vertex(pose.position, va.norm, va.uv, va.col);
						int newV = mesh.AddVertex(newVertex);

						mesh.AddEdge(new GEdge(e.a, newV));
						mesh.AddEdge(new GEdge(e.b, newV));

						mesh.AddFace(new GFace(newV, e.a, e.b));

						Grab(mesh, new GMeshPart(GMeshPartType.Vertex, newV), pose);
						break;
					}
				case GMeshPartType.Face:
					{
						GFace f = mesh.faces.values[part.Value.Index];

						Vertex va = mesh.vertices.values[f.a];
						Vertex newVertex = new Vertex(pose.position, va.norm, va.uv, va.col);
						int newV = mesh.AddVertex(newVertex);

						mesh.AddEdge(new GEdge(f.a, newV));
						mesh.AddEdge(new GEdge(f.b, newV));
						mesh.AddEdge(new GEdge(f.c, newV));

						mesh.RemoveFace(part.Value.Index);

						mesh.AddFace(new GFace(newV, f.a, f.b));
						mesh.AddFace(new GFace(newV, f.b, f.c));
						mesh.AddFace(new GFace(newV, f.c, f.a));

						Grab(mesh, new GMeshPart(GMeshPartType.Vertex, newV), pose);
						break;
					}
			}
		}

		lastInput = input;
	}

	public void DrawUI()
	{
		UI.Text($"Hand {handed}");
		UI.Text($"State: {lastInput.state}, Held: {held}");
	}
}

class Program
{
	public static void Main()
	{
		// Initialize StereoKit
		SKSettings settings = new SKSettings
		{
			appName = "gesh",
		};
		if (!SK.Initialize(settings))
			return;


		// Create assets used by the app
		Pose cubePose = new Pose(0, 0, -0.5f);
		Model cube = Model.FromMesh(
			Mesh.GenerateRoundedCube(Vec3.One * 0.1f, 0.02f),
			Material.UI);

		Matrix floorTransform = Matrix.TS(0, -1.5f, 0, new Vec3(30, 0.1f, 30));
		Material floorMaterial = new Material("floor.hlsl");
		floorMaterial.Transparency = Transparency.Blend;

		// cube.RootNode.Mesh.SetData()

		GEditableMesh editableMesh = new GEditableMesh();
		editableMesh.AddVertex(new Vertex(new Vec3(0, -0.5f, -0.5f), Vec3.Up));
		editableMesh.AddVertex(new Vertex(new Vec3(0, 0.5f, -0.5f), Vec3.Up));
		editableMesh.AddVertex(new Vertex(new Vec3(1.0f, 0f, -0.5f), Vec3.Up));
		editableMesh.AddEdge(new GEdge(0, 1));
		editableMesh.AddEdge(new GEdge(1, 2));
		editableMesh.AddEdge(new GEdge(2, 0));
		// editableMesh.AddFace(new GFace(0, 1, 2));
		editableMesh.AddFace(new GFace(2, 1, 0));

		GHand left = new GHand(Handed.Left);
		GHand right = new GHand(Handed.Right);

		// Core application loop
		Pose uiPose = new(0, 0, -0.5f, Quat.LookDir(-Vec3.Forward));
		SK.Run(() =>
		{
			if (Device.DisplayBlend == DisplayBlend.Opaque)
				Mesh.Cube.Draw(floorMaterial, floorTransform);

			left.Tick(editableMesh);
			right.Tick(editableMesh);
			// UI.Handle("Cube", ref cubePose, cube.Bounds);
			// cube.Draw(cubePose.ToMatrix());
			editableMesh.Draw();

			Hand hand = Input.Hand(Handed.Right);
			Vec3 handPos = hand[FingerId.Index, JointId.Tip].position;

			GMeshPart? part = editableMesh.GetClosestPart(handPos);
			if (part.HasValue)
			{
				switch (part?.Type)
				{
					case GMeshPartType.Vertex:
						{
							var color = Color.Hex(0xFF0000FF);
							float thickness = 0.05f;
							Lines.Add(handPos, editableMesh.vertices.values[part.Value.Index].pos, color, thickness);
							break;
						}
					case GMeshPartType.Edge:
						{

							var color = Color.Hex(0x00FF00FF);
							float thickness = 0.05f;
							var e = editableMesh.edges.values[part.Value.Index];
							Lines.Add(handPos, editableMesh.vertices.values[e.a].pos, color, thickness);
							Lines.Add(handPos, editableMesh.vertices.values[e.b].pos, color, thickness);
							break;
						}
					case GMeshPartType.Face:
						{
							var color = Color.Hex(0x0000FFFF);
							float thickness = 0.05f;
							var f = editableMesh.faces.values[part.Value.Index];
							Lines.Add(handPos, editableMesh.vertices.values[f.a].pos, color, thickness);
							Lines.Add(handPos, editableMesh.vertices.values[f.b].pos, color, thickness);
							Lines.Add(handPos, editableMesh.vertices.values[f.c].pos, color, thickness);
							break;
						}
				}
			}
			UI.WindowBegin("Simple Window", ref uiPose);
			left.DrawUI();
			right.DrawUI();
			UI.WindowEnd();
		});
	}
}