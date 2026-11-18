using System;
using StereoKit;

namespace gesh;

/// <summary>
/// A menu that shows up in front of a hand when the user looks at its open
/// palm with the fingers stretched out. The other hand pokes it. It can be
/// dragged off the palm and left in the world.
///
/// A strip of tools sits at the palm, and above it a panel with the current
/// tool's options, as tall as that tool needs. The strip, the panel and the
/// fingertip are all views of settings.tool. The strip's last button opens
/// Home (files, settings, info), which lives in the world, not on the hand.
/// See plans/ui-design.md and its mockups.
/// </summary>
class GPalmMenu
{
	// Facing is the dot product between the palm normal and the direction to
	// the head, straightness the dot product between each finger and the hand's
	// forward direction. Opening is stricter than staying open, so the menu
	// doesn't flicker at the edges.
	const float OPEN_FACING = 0.8f;
	const float STAY_FACING = 0.4f;
	/// <summary>Opening also needs the head pointed at the palm, within this
	/// many degrees: a palm turned up at the edge of view while working
	/// summoned it by accident.</summary>
	const float OPEN_GAZE_DEGREES = 37.5f;
	/// <summary>It stays open until the head turns twice as far from the palm
	/// as opening needs: glancing at the model while poking the menu keeps
	/// it, looking away puts it away.</summary>
	const float STAY_GAZE_DEGREES = OPEN_GAZE_DEGREES * 2;
	const float STAY_STRAIGHTNESS_MARGIN = 0.2f;

	public const float WIDTH = 0.19f;
	static readonly (GTool tool, GIcon icon)[] TOOLS = [(GTool.Edit, GIcon.Edit), (GTool.Select, GIcon.Select), (GTool.Delete, GIcon.Delete), (GTool.Brush, GIcon.Brush), (GTool.Fill, GIcon.Fill)];
	public const float STRIP_BUTTON = 0.032f;
	public static float StripHeight => STRIP_BUTTON + UI.Settings.margin * 2;
	/// <summary>A window around a row of strip buttons.</summary>
	public static float RowWidth(int buttons) => buttons * STRIP_BUTTON + (buttons - 1) * UI.Settings.gutter + UI.Settings.margin * 2;
	public const float GAP = 0.008f;
	/// <summary>The usual button height in the menu, a comfortable poke.</summary>
	public const float BUTTON = 0.032f;
	/// <summary>How far the menu leans back from vertical, for reading.</summary>
	const float TILT_DEGREES = 30;

	/// <summary>Which hand the menu is showing on, if any.</summary>
	Handed? menuHand;
	/// <summary>Where the menu was left in the world, if it was dragged off
	/// the palm: the top center of the strip. While set, the menu stays there
	/// instead of following a hand.</summary>
	Pose? placedPose;
	readonly GPaintTab paintTab = new();

	/// <summary>The hand the menu is on, while it's on one.</summary>
	public Handed? MenuHand => placedPose.HasValue ? null : menuHand;

	/// <summary>Whether the menu is showing, on a hand or in the world.</summary>
	public bool Shown => placedPose.HasValue || menuHand.HasValue;

	/// <returns>Whether undo or redo changed the mesh.</returns>
	public bool Tick(GSettings settings, GDocument doc, GHome home)
	{
		Pose strip;
		if (placedPose.HasValue)
		{
			strip = placedPose.Value;
		}
		else
		{
			menuHand = PickMenuHand(menuHand, settings);
			if (!menuHand.HasValue) return false;
			strip = PalmPose(Input.Hand(menuHand.Value));
		}
		return Draw(strip, settings, doc, home);
	}

	/// <summary>The panel above the strip, as tall as the tool's options
	/// need. Null for a tool without options.</summary>
	static float? PanelHeight(GTool tool) => tool switch
	{
		// Two of its legends take two lines.
		GTool.Edit => 0.168f,
		GTool.Select => BUTTON + UI.Settings.margin * 2,
		GTool.Delete => null,
		GTool.Brush or GTool.Fill => GPaintTab.Height,
		_ => throw new ArgumentException($"No panel for {tool}"),
	};

	/// <summary>Draws the menu with the strip's top center at `strip`.</summary>
	/// <returns>Whether undo or redo changed the mesh.</returns>
	public bool Draw(Pose strip, GSettings settings, GDocument doc, GHome home)
	{
		float? height = PanelHeight(settings.tool);
		// The top of everything: the panel's, or the strip's without one.
		Pose top = strip;
		if (height.HasValue)
		{
			Vec3 up = strip.orientation * Vec3.Up;
			Pose panel = new(strip.position + up * (GAP + height.Value), strip.orientation);
			Pose startPanel = panel;
			UI.WindowBegin("gesh", ref panel, new Vec2(WIDTH, height.Value), UIWin.Body, placedPose.HasValue ? UIMove.FaceUser : UIMove.Exact);
			// Dragging the panel off the palm leaves the menu in the world. A
			// small tolerance, in case StereoKit nudges the pose without a drag.
			if (placedPose.HasValue || (panel.position - startPanel.position).LengthSq > 0.001f * 0.001f)
			{
				strip = new Pose(panel.position - panel.orientation * Vec3.Up * (GAP + height.Value), panel.orientation);
				placedPose = strip;
				menuHand = null;
			}

			switch (settings.tool)
			{
				case GTool.Edit: DrawEditOptions(doc); break;
				case GTool.Select: DrawSelectOptions(doc.mesh); break;
				case GTool.Brush or GTool.Fill: paintTab.Draw(settings); break;
				default: throw new InvalidOperationException($"No options for {settings.tool}");
			}
			UI.WindowEnd();
			top = panel;
		}

		bool rewound = DrawStrip(strip, settings, doc.history, home);
		if (placedPose.HasValue) DrawClose(top, height.HasValue ? WIDTH : StripWidth);
		return rewound;
	}

	/// <summary>Width of the tools window, centered under the panel.</summary>
	static float ToolsWidth => RowWidth(TOOLS.Length);
	/// <summary>Width of the undo and redo window, right of the tools.</summary>
	static float UndoWidth => RowWidth(2);

	/// <summary>The tools, centered under the panel, with Home floating off
	/// to their left and undo and redo to their right, since those aren't
	/// tools. No backgrounds: the buttons stand on their own.</summary>
	/// <returns>Whether undo or redo changed the mesh.</returns>
	bool DrawStrip(Pose pose, GSettings settings, GHistory history, GHome home)
	{
		float width = ToolsWidth;
		Vec2 button = new(STRIP_BUTTON, STRIP_BUTTON);
		Pose tools = pose;
		UI.WindowBegin("gesh tools", ref tools, new Vec2(width, StripHeight), UIWin.Empty, UIMove.None);
		foreach (var (tool, icon) in TOOLS)
		{
			Sprite sprite = GIcons.Get(icon);
			// The current tool is the one blue button.
			bool current = settings.tool == tool;
			if (GTheme.Colored(current ? GTheme.ACCENT : null, () => UI.Radio(tool.ToString(), current, sprite, sprite, UIBtnLayout.CenterNoText, button)))
			{
				settings.tool = tool;
				settings.picking = false;
			}
			UI.SameLine();
		}
		UI.WindowEnd();

		// Window layout x grows to the user's left, so their right is -x.
		float homeWidth = RowWidth(1);
		Pose homePose = new(pose.position + pose.orientation * new Vec3(width / 2 + GAP + homeWidth / 2, 0, 0), pose.orientation);
		UI.WindowBegin("gesh home button", ref homePose, new Vec2(homeWidth, StripHeight), UIWin.Empty, UIMove.None);
		if (UI.ButtonImg("Home", GIcons.Get(GIcon.Home), UIBtnLayout.CenterNoText, button)) home.Toggle();
		UI.WindowEnd();

		Pose undoPose = new(pose.position + pose.orientation * new Vec3(-(width / 2 + GAP + UndoWidth / 2), 0, 0), pose.orientation);
		UI.WindowBegin("gesh undo", ref undoPose, new Vec2(UndoWidth, StripHeight), UIWin.Empty, UIMove.None);
		bool rewound = false;
		UI.PushEnabled(history.CanUndo);
		if (UI.ButtonImg("Undo", GIcons.Get(GIcon.Undo), UIBtnLayout.CenterNoText, button)) rewound |= history.Undo();
		UI.PopEnabled();
		UI.SameLine();
		UI.PushEnabled(history.CanRedo);
		if (UI.ButtonImg("Redo", GIcons.Get(GIcon.Redo), UIBtnLayout.CenterNoText, button)) rewound |= history.Redo();
		UI.PopEnabled();
		UI.WindowEnd();
		return rewound;
	}

	/// <summary>The whole strip: the tools with Home and undo and redo either
	/// side.</summary>
	static float StripWidth => ToolsWidth + (GAP + UndoWidth) * 2;

	/// <summary>The close button, while the menu is left in the world: off
	/// the right side of its top part, level with its top.</summary>
	/// <param name="top">The top center of the menu: the panel's, or the
	/// strip's when the tool has no panel.</param>
	/// <param name="width">The width of that top part.</param>
	void DrawClose(Pose top, float width)
	{
		if (GDraw.Close("gesh close", GDraw.TopRight(top, width))) placedPose = null;
	}

	/// <summary>In front of the palm, the bottom of the strip at the bottom of
	/// the palm, turned toward the user and leaning back a little.</summary>
	static Pose PalmPose(Hand hand)
	{
		Vec3 bottom = hand.palm.position + hand.palm.Forward * 0.06f - hand.wrist.Forward * 0.04f;
		// Turned toward the middle of the head at the menu's own height, so
		// it only turns left and right, then leaned back.
		Vec3 head = GDraw.HeadCenter;
		Quat facing = Quat.LookAt(bottom, new Vec3(head.x, bottom.y, head.z));
		// Tilted about the menu's own x axis: the other order tilts about the
		// world's, which rolls the menu when the user isn't facing -z.
		Quat tilted = facing * Quat.FromAngles(TILT_DEGREES, 0, 0);
		return new Pose(bottom + tilted * Vec3.Up * StripHeight, tilted);
	}

	/// <summary>How to use the edit tool: the gestures can't be discovered
	/// any other way.</summary>
	static void DrawEditOptions(GDocument doc)
	{
		Legend(GIcon.Grab, "Pinch to grab");
		Legend(GIcon.Extend, "Pinch with middle finger to pull out a new part");
		Legend(GIcon.Add, "Pinch with middle finger twice to add a vertex");
		Legend(GIcon.Move, "Pinch the air to move");
		if (UI.Button("Reset view", new Vec2(0.07f, BUTTON))) doc.world = GWorld.Identity;
	}

	/// <summary>The selection's size, and what to do with all of it.</summary>
	static void DrawSelectOptions(GEditableMesh mesh)
	{
		const float CLEAR = 0.07f;
		bool any = mesh.selected.Count > 0;
		UI.Text(any ? $"{mesh.selected.Count} selected" : "Nothing selected", Align.CenterLeft, TextFit.Squeeze, new Vec2(UI.LayoutRemaining.x - BUTTON - CLEAR - UI.Settings.gutter * 2, BUTTON));
		UI.SameLine();
		UI.PushEnabled(any);
		if (UI.ButtonImg("delete", GIcons.Get(GIcon.Delete), UIBtnLayout.CenterNoText, new Vec2(BUTTON, BUTTON))) mesh.RemoveSelected();
		UI.SameLine();
		if (UI.ButtonImg("Clear", GIcons.Get(GIcon.Close), UIBtnLayout.Left, new Vec2(CLEAR, BUTTON))) mesh.selected.Clear();
		UI.PopEnabled();
	}

	/// <summary>An icon and a line saying what a gesture does, in small
	/// print, wrapped onto a second line if it doesn't fit on one. The icon
	/// stays centered beside the text.</summary>
	public static void Legend(GIcon icon, string text)
	{
		const float ICON = 0.02f;
		TextStyle style = GTheme.Small;
		float width = UI.LayoutRemaining.x - ICON - UI.Settings.gutter;
		float height = float.Max(UI.LineHeight, Text.SizeLayout(text, style, width).y + UI.Settings.padding);
		Bounds b = UI.LayoutReserve(new Vec2(ICON, height));
		GDraw.Shape(icon, b.center, new Vec2(ICON, ICON), Color.White);
		UI.SameLine();
		UI.PushTextStyle(style);
		UI.Text(text, Align.CenterLeft, TextFit.Wrap, new Vec2(UI.LayoutRemaining.x, height));
		UI.PopTextStyle();
	}

	static Handed? PickMenuHand(Handed? current, GSettings settings)
	{
		float stayStraightness = settings.menuStraightness - STAY_STRAIGHTNESS_MARGIN;
		if (current.HasValue && IsPalmShown(Input.Hand(current.Value), STAY_FACING, stayStraightness) && IsLookedAt(Input.Hand(current.Value), STAY_GAZE_DEGREES))
		{
			return current;
		}

		foreach (Handed handed in new[] { Handed.Left, Handed.Right })
		{
			Hand hand = Input.Hand(handed);
			if (IsPalmShown(hand, OPEN_FACING, settings.menuStraightness) && IsLookedAt(hand, OPEN_GAZE_DEGREES)) return handed;
		}
		return null;
	}

	static bool IsLookedAt(Hand hand, float maxDegrees)
	{
		Vec3 toPalm = (hand.palm.position - Input.Head.position).Normalized;
		return Vec3.Dot(Input.Head.Forward, toPalm) >= MathF.Cos(maxDegrees * MathF.PI / 180);
	}

	/// <summary>Is the palm facing the user, with the fingers stretched out
	/// rather than curled?</summary>
	static bool IsPalmShown(Hand hand, float minFacing, float minStraightness)
	{
		if (!hand.IsTracked) return false;

		Vec3 toHead = (Input.Head.position - hand.palm.position).Normalized;
		if (Vec3.Dot(hand.palm.Forward, toHead) < minFacing) return false;

		Vec3 handForward = hand.wrist.Forward;
		foreach (FingerId finger in new[] { FingerId.Index, FingerId.Middle, FingerId.Ring, FingerId.Little })
		{
			Vec3 fingerDir = (hand[finger, JointId.Tip].position - hand[finger, JointId.KnuckleMajor].position).Normalized;
			if (Vec3.Dot(fingerDir, handForward) < minStraightness) return false;
		}
		return true;
	}
}
