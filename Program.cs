using System.Collections.Generic;
using StereoKit;
using StereoKit.Framework;

namespace gesh;

class Program
{
	const float FLOOR_SIZE = 30;
	/// <summary>Display refresh rate to ask the headset for, see GRefreshRate.</summary>
	const float REFRESH_RATE = 120;
	/// <summary>Size of the fill tool's box on the fingertip. A face within
	/// half of it is touched.</summary>
	const float FILL_SIZE = 0.025f;

	/// <summary>Where the hand is pinching empty air, irl. A pinch that grabbed
	/// part of the mesh isn't an air pinch, and neither is one whose fingers
	/// haven't closed tightly yet.</summary>
	static Pose? AirPinch(GHand hand, GHandInput input, GSettings settings)
	{
		bool air = input.state == GHandState.IndexPinch && !hand.PinchUsed && input.closest <= settings.airPinchGap;
		return air ? input.pose : null;
	}

	/// <summary>While both hands zoom, the scale between the fingers, on a
	/// pill like the rest of the UI.</summary>
	static void DrawZoom(Vec3? left, Vec3? right, float scale)
	{
		if (!left.HasValue || !right.HasValue) return;
		Vec3 mid = (left.Value + right.Value) / 2;
		Pose pose = new(mid, Quat.LookAt(mid, Input.Head.position));
		UI.WindowBegin("gesh zoom", ref pose, Vec2.Zero, UIWin.Body, UIMove.None);
		UI.Label($"{scale * 100:0}%");
		UI.WindowEnd();
	}

	/// <summary>A hand touching the menu is using the menu, so pinches it
	/// starts there shouldn't grab parts or move the world.</summary>
	/// <summary>This tick's sounds: what the hands did, and what changed.
	/// The volume setting covers StereoKit's menu sounds too.</summary>
	static void PlaySounds(GSounds sounds, GSettings settings, GHand left, GHand right, Pose? leftAir, Pose? rightAir, GQuickPalette leftPalette, GQuickPalette rightPalette, GPalmMenu menu, GWorld world, Matrix toWorld)
	{
		Audio.SetBusVolume(SoundBus.Sfx, settings.soundVolume);
		Audio.SetBusVolume(SoundBus.Ui, settings.soundVolume);
		sounds.Hand(left.Events, toWorld);
		sounds.Hand(right.Events, toWorld);
		sounds.Air(leftAir, rightAir, world.scale);
		sounds.Palette(Handed.Left, leftPalette);
		sounds.Palette(Handed.Right, rightPalette);
		sounds.Menu(menu.Shown, menu.MenuHand is Handed h ? Input.Hand(h).palm.position : Input.Head.position);
		sounds.Brush(Handed.Left, left.Brushing, Input.Hand(Handed.Left)[FingerId.Index, JointId.Tip].position);
		sounds.Brush(Handed.Right, right.Brushing, Input.Hand(Handed.Right)[FingerId.Index, JointId.Tip].position);
	}

	static GHandInput ReadInput(GHand hand, Handed handed, GSettings settings)
	{
		InteractorSource source = handed == Handed.Left ? InteractorSource.HandLeft : InteractorSource.HandRight;
		return hand.ReadInput(settings, Interactor.IsInteracting(source));
	}

	/// <summary>The hand holding the menu, or poking it, is busy with it.</summary>
	static bool Busy(Handed handed, GPalmMenu menu)
	{
		InteractorSource source = handed == Handed.Left ? InteractorSource.HandLeft : InteractorSource.HandRight;
		return menu.MenuHand == handed || Interactor.IsInteracting(source);
	}

	/// <summary>The paint tool for a hand. A busy hand doesn't paint.</summary>
	static GPaintTool PaintTool(Handed handed, GSettings settings, GPalmMenu menu, float meshScale)
	{
		if (settings.tool == GTool.Edit) return GPaintTool.Edit;
		float radius = settings.tool == GTool.Fill ? FILL_SIZE / 2 : settings.brushRadius;
		return new GPaintTool(settings.tool, radius / meshScale, settings.brushColor, settings.picking, active: !Busy(handed, menu));
	}

	/// <summary>Shows on the index fingertip what the next pinch will do.
	/// Editing: the whole part it would grab highlighted, so a pinch that misses
	/// doesn't move the world by surprise. Brush: the sphere it paints with.
	/// Fill: a box, so it's obviously different. Pipette: a small sphere in
	/// the color under the fingertip.</summary>
	static void DrawFingertip(Handed handed, GHandInput input, GSettings settings, GPalmMenu menu, Material material, GPartHighlight highlight, GDocument doc, float grabRadius)
	{
		Hand hand = Input.Hand(handed);
		if (!hand.IsTracked || Busy(handed, menu)) return;
		Matrix toMesh = doc.world.Matrix.Inverse;
		Pose tip = hand[FingerId.Index, JointId.Tip].Pose;
		if (settings.tool is GTool.Edit or GTool.Select or GTool.Delete)
		{
			// While pinching, the part itself shows where it goes. Red for
			// what a pinch would delete.
			if (input.state != GHandState.Open) return;
			GMeshPart? part = doc.mesh.GetClosestPart(toMesh.Transform(input.pose.position), grabRadius);
			if (!part.HasValue) return;
			Color mark = (settings.tool == GTool.Delete ? GTheme.DANGER : GTheme.ACCENT).ToLinear();
			highlight.Draw(doc.mesh, part.Value, doc.world.Matrix, doc.world.scale, settings, new Color(mark.r, mark.g, mark.b, 0.45f));
			return;
		}

		Color32 paint = settings.picking
			? doc.mesh.ColorAt(toMesh.Transform(tip.position), grabRadius) ?? new Color32(128, 128, 128, 255)
			: settings.brushColor;
		Color color = GPaintTab.Linear(paint);
		Color shown = new(color.r, color.g, color.b, settings.picking ? 1 : 0.5f);
		if (settings.picking) Mesh.Sphere.Draw(material, Matrix.TS(tip.position, 0.02f), shown);
		else if (settings.tool == GTool.Brush) Mesh.Sphere.Draw(material, Matrix.TS(tip.position, settings.brushRadius * 2), shown);
		else if (settings.tool == GTool.Fill) Mesh.Cube.Draw(material, Matrix.TRS(tip.position, tip.orientation, FILL_SIZE), shown);
		else throw new System.InvalidOperationException($"No fingertip for {settings.tool}");
	}

	/// <summary>The quick palette is for painting hands that aren't busy.</summary>
	static Color32? QuickPalette(GQuickPalette palette, Handed handed, GHandInput input, GSettings settings, GPalmMenu menu)
	{
		bool allowed = settings.tool is GTool.Brush or GTool.Fill && !settings.picking && Input.Hand(handed).IsTracked && !Busy(handed, menu);
		return palette.Tick(input, allowed, settings.brushColor, settings.recentColors);
	}

	public static void Main()
	{
		// Initialize StereoKit
		SKSettings settings = new SKSettings
		{
			appName = "gesh",
		};
		GRefreshRate.RequestExtension();
		// Off until the settings say otherwise, see plans/mr.md.
		PassthroughFBExt passthrough = SK.AddStepper(new PassthroughFBExt(enabled: false));
		GMetaHandMesh handMesh = SK.AddStepper(new GMetaHandMesh());
		if (!SK.Initialize(settings))
			return;
		GRefreshRate.Apply(REFRESH_RATE);
		GTheme.Apply();
		GIcons.Preload();
		GSounds sounds = new();
		GControllerNotice controllerNotice = new();


		Matrix floorTransform = Matrix.TS(0, -1.5f, 0, new Vec3(FLOOR_SIZE, 0.1f, FLOOR_SIZE));
		Material floorMaterial = new Material("floor.hlsl")
		{
			Transparency = Transparency.Blend
		};
		floorMaterial["size"] = FLOOR_SIZE;


		var lights = new List<SHLight>
		{
			new() {directionTo = Vec3.Up, color = Color.White}
		};
		SphericalHarmonics lighting = SphericalHarmonics.FromLights(lights.ToArray());

		// Renderer.SkyTex = Tex.GenCubemap(lighting);
		// Renderer.SkyLight = lighting;

		// cube.RootNode.Mesh.SetData()

		// A bench run measures performance on a scripted scene, see plans/perf.md.
		GBench? bench = GBench.FromFlag();
		bool firstLaunch = bench == null && GSaver.IsFirstLaunch();
		if (firstLaunch) GSaver.AddSamples();
		GDocument doc = bench != null ? bench.CreateDocument() : firstLaunch ? GDocument.New() : GSaver.StartupDocument();
		GAutosave autosave = new(doc);
		GMeshRenderer meshRenderer = new();
		GSettings gsettings = GSettingsFile.Load();
		GSettingsFile settingsFile = new(gsettings);
		GHand left = new(Handed.Left);
		GHand right = new(Handed.Right);
		GWorldNav nav = new();
		GTwoHandGrab twoHandGrab = new();
		GPalmMenu menu = new();
		GHome home = new(sounds);
		GQuickPalette leftPalette = new(), rightPalette = new();
		GHints hints = new(firstLaunch, doc);
		bool wasActing = false;
		Material brushMaterial = Material.Unlit.Copy();
		brushMaterial.Transparency = Transparency.Blend;
		// One per hand: each refills its own face triangle every frame.
		GPartHighlight leftHighlight = new(), rightHighlight = new();
		GHandColors handColors = new();
		handMesh.Colors = handColors;

		// Hand rays poking at the menu from afar would fight with pinching.
		UI.EnableFarInteract = false;

		SK.Run(() =>
		{
			bench?.BeginFrame();
			bool rewound = menu.Tick(gsettings, doc, home);
			GDocument shown = home.Tick(gsettings, doc, hints, passthrough.Available, handMesh.Available);
			passthrough.Enabled = gsettings.passthrough;
			handMesh.Enabled = gsettings.metaHandMesh;
			handColors.Tick(gsettings);
			if (shown != doc || rewound)
			{
				// A file was opened, a new one started, or undo or redo changed
				// the mesh. Whatever the hands held isn't there any more.
				doc = shown;
				left = new(Handed.Left);
				right = new(Handed.Right);
				twoHandGrab = new();
			}

			bench?.Mark("menu");
			GHandInput leftInput = ReadInput(left, Handed.Left, gsettings);
			GHandInput rightInput = ReadInput(right, Handed.Right, gsettings);

			// Navigation happens first, so held parts follow this frame's world.
			Pose? leftAir = AirPinch(left, leftInput, gsettings), rightAir = AirPinch(right, rightInput, gsettings);
			doc.world = nav.Tick(doc.world, leftAir, rightAir, gsettings.minWorldScale, gsettings.maxWorldScale, gsettings.turnSmoothing);
			DrawZoom(leftAir?.position, rightAir?.position, doc.world.scale);

			// The hands work in mesh space, and don't know the world moves.
			Matrix toWorld = doc.world.Matrix;
			Matrix toMesh = toWorld.Inverse;
			float grabRadius = gsettings.grabRadius / doc.world.scale;
			Vec3 viewer = toMesh.Transform(Input.Head.position);
			left.Tick(doc.mesh, leftInput.Transformed(toMesh), grabRadius, viewer, PaintTool(Handed.Left, gsettings, menu, doc.world.scale));
			right.Tick(doc.mesh, rightInput.Transformed(toMesh), grabRadius, viewer, PaintTool(Handed.Right, gsettings, menu, doc.world.scale));
			// Both hands holding the same thing (the selection, or a part) move,
			// turn and scale it together.
			twoHandGrab.Tick(doc.mesh, toWorld, left, leftInput.pose, right, rightInput.pose, gsettings);
			if (twoHandGrab.Active) DrawZoom(leftInput.pose.position, rightInput.pose.position, twoHandGrab.Scale);
			// A picked color becomes the paint color, and painting carries on.
			Color32? picked = left.Picked ?? right.Picked;
			if (picked.HasValue)
			{
				gsettings.brushColor = picked.Value;
				gsettings.picking = false;
			}
			if (left.Painting || right.Painting) gsettings.UsedColor(gsettings.brushColor);
			// A double pinch in the air goes back to Edit from any tool, and
			// from Edit back to the tool it left. Leaving Select is also done
			// selecting.
			if (left.AirDoubleTapped || right.AirDoubleTapped)
			{
				GTool? to = gsettings.tool == GTool.Edit ? gsettings.lastTool : GTool.Edit;
				if (to.HasValue)
				{
					if (gsettings.tool == GTool.Select) doc.mesh.selected.Clear();
					gsettings.tool = to.Value;
					gsettings.picking = false;
				}
			}
			if (gsettings.tool != GTool.Edit) gsettings.lastTool = gsettings.tool;
			Color32? quick = QuickPalette(leftPalette, Handed.Left, leftInput, gsettings, menu) ?? QuickPalette(rightPalette, Handed.Right, rightInput, gsettings, menu);
			if (quick.HasValue) gsettings.brushColor = quick.Value;
			DrawFingertip(Handed.Left, leftInput, gsettings, menu, brushMaterial, leftHighlight, doc, grabRadius);
			DrawFingertip(Handed.Right, rightInput, gsettings, menu, brushMaterial, rightHighlight, doc, grabRadius);
			PlaySounds(sounds, gsettings, left, right, leftAir, rightAir, leftPalette, rightPalette, menu, doc.world, toWorld);
			sounds.Grab(twoHandGrab.Active, twoHandGrab.Scale, leftInput.pose, rightInput.pose);
			hints.Tick(doc, menu.Shown);
			controllerNotice.Tick();

			// Each time both hands stop changing the document (a slider drag
			// counts), what changed becomes one undo step.
			bool acting = left.Acting || right.Acting || Interactor.IsInteracting(InteractorSource.HandLeft) || Interactor.IsInteracting(InteractorSource.HandRight);
			if (wasActing && !acting) doc.history.Commit();
			wasActing = acting;
			bench?.Mark("hands");

			// The floor lives in mesh space, so it moves with the world. Over
			// passthrough the real floor is there instead.
			if (!passthrough.Enabled)
				Mesh.Cube.Draw(floorMaterial, floorTransform * toWorld, Color.White, GSaver.SCENE_LAYER);
			meshRenderer.Draw(doc.mesh, toWorld, gsettings);
			bench?.Mark("draw");

			// Losing focus can be the app being closed, so save work then too.
			// Bench runs aren't work, and saving would skew their timing.
			if (bench == null) autosave.Tick(doc, now: SK.AppFocus != AppFocus.Active);
			settingsFile.Tick(gsettings);
			bench?.EndFrame(doc, gsettings);

			// Hand hand = Input.Hand(Handed.Right);
			// Vec3 handPos = hand[FingerId.Index, JointId.Tip].position;

			// GMeshPart? part = editableMesh.GetClosestPart(handPos);
			// if (part.HasValue)
			// {
			// 	switch (part?.Type)
			// 	{
			// 		case GMeshPartType.Vertex:
			// 			{
			// 				var color = Color.Hex(0xFF0000FF);
			// 				float thickness = 0.05f;
			// 				Lines.Add(handPos, editableMesh.vertices.values[part.Value.Index].pos, color, thickness);
			// 				break;
			// 			}
			// 		case GMeshPartType.Edge:
			// 			{

			// 				var color = Color.Hex(0x00FF00FF);
			// 				float thickness = 0.05f;
			// 				var e = editableMesh.edges.values[part.Value.Index];
			// 				Lines.Add(handPos, editableMesh.vertices.values[e.a].pos, color, thickness);
			// 				Lines.Add(handPos, editableMesh.vertices.values[e.b].pos, color, thickness);
			// 				break;
			// 			}
			// 		case GMeshPartType.Face:
			// 			{
			// 				var color = Color.Hex(0x0000FFFF);
			// 				float thickness = 0.05f;
			// 				var f = editableMesh.faces.values[part.Value.Index];
			// 				Lines.Add(handPos, editableMesh.vertices.values[f.a].pos, color, thickness);
			// 				Lines.Add(handPos, editableMesh.vertices.values[f.b].pos, color, thickness);
			// 				Lines.Add(handPos, editableMesh.vertices.values[f.c].pos, color, thickness);
			// 				break;
			// 			}
			// 	}
			// }


			// UI.WindowBegin("Simple Window", ref uiPose);
			// editableMesh.DrawUI();
			// UI.HSeparator();
			// left.DrawUI();
			// UI.HSeparator();
			// right.DrawUI();
			// UI.WindowEnd();
		});

		if (bench == null) autosave.Tick(doc, now: true);
		settingsFile.Tick(gsettings, now: true);
	}
}