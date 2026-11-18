using System.Collections.Generic;
using System.Text.Json.Serialization;
using StereoKit;

namespace gesh;

/// <summary>What the hands do to the mesh.</summary>
enum GTool
{
	/// <summary>Grab, pull out and merge parts.</summary>
	Edit,
	/// <summary>Sweep parts into the selection, which Edit then moves together.</summary>
	Select,
	/// <summary>Paint wherever the fingertip's sphere touches.</summary>
	Brush,
	/// <summary>Paint whole faces the fingertip touches.</summary>
	Fill,
	/// <summary>Pinch a part to delete it.</summary>
	Delete,
}

/// <summary>App-wide tweakables. One instance is created in Main and passed to
/// whatever needs it, and saved between runs by GSettingsFile. Distances are
/// real-world meters unless noted.</summary>
class GSettings
{
	/// <summary>Gap between the thumb and fingertip skin that starts a
	/// pinch. A gap, not the distance between the joints, like StereoKit's own
	/// pinch: the tracked joint radii fit the hand, so a thick middle finger
	/// doesn't have to squash closer than the index.</summary>
	public float pinchStartGap = 0.01f;
	/// <summary>Thumb to fingertip gap that ends a pinch. Larger than
	/// pinchStartGap so pinches don't flicker.</summary>
	public float pinchEndGap = 0.02f;
	/// <summary>Instead of pinchEndGap, a pinch releases once the fingers
	/// open pinchReleaseDelta past the closest they got during the pinch.</summary>
	public bool experimentalPinch = true;
	public float pinchReleaseDelta = 0.0046f;

	/// <summary>How straight the fingers need to be to open the palm menu, as
	/// the dot product between each finger and the hand's direction. Once
	/// open, it stays open down to this minus a margin.</summary>
	public float menuStraightness = 0.75f;

	/// <summary>How close a pinch must be to a vertex/edge/face to grab it.
	/// Stays the same irl distance no matter how the world is zoomed.</summary>
	public float grabRadius = 0.05f;

	/// <summary>Diameter of the vertex handles, in mesh space so it scales
	/// with the world.</summary>
	public float vertexSize = 0.03f;
	/// <summary>Diameter of the edge cylinders, in mesh space.</summary>
	public float edgeThickness = 0.01f;
	/// <summary>Scales the vertex handles and edges as drawn, an
	/// experimental setting while the right size is found.</summary>
	public float outlineScale = 0.5f;
	/// <summary>Seconds the hands' turning is smoothed over when they move
	/// the world or a selection, see GWorldNav. Experimental.</summary>
	public float turnSmoothing = 0.02f;
	/// <summary>The vertex handles' diameter as drawn.</summary>
	[JsonIgnore] public float VertexDrawSize => vertexSize * outlineScale;
	/// <summary>The edges' diameter as drawn.</summary>
	[JsonIgnore] public float EdgeDrawThickness => edgeThickness * outlineScale;

	/// <summary>A pinch only moves the world once the fingers have closed to
	/// this gap, tighter than pinchStartGap, so a loosely closed hand
	/// doesn't drag the world by accident.</summary>
	public float airPinchGap = 0.0061f;
	public float minWorldScale = 0.05f;
	public float maxWorldScale = 20.0f;

	/// <summary>Show the room around the model, where the headset can.</summary>
	public bool passthrough = true;
	/// <summary>Draw Meta's own hand mesh instead of StereoKit's, where the
	/// headset has it.</summary>
	public bool metaHandMesh = true;
	/// <summary>Draw the mesh's vertices and edges over its faces, except
	/// while painting. Not saved: it's only switchable in the experimental
	/// settings, so it starts on every launch even if it was turned off.</summary>
	[JsonIgnore] public bool meshOutline = true;
	/// <summary>How loud the app's sounds are, 0 to 1, StereoKit's menu
	/// sounds included. 0 is silent.</summary>
	public float soundVolume = 1;

	/// <summary>Not saved: the app starts in Edit.</summary>
	[JsonIgnore] public GTool tool = GTool.Edit;
	/// <summary>The last tool other than Edit, which a double pinch in the
	/// air in Edit goes back to. Null until one's been used.</summary>
	[JsonIgnore] public GTool? lastTool;
	/// <summary>The pipette is armed: the next touch of the open fingertip on
	/// the mesh picks up its color, then the paint tool carries on. Only for Brush and Fill.</summary>
	[JsonIgnore] public bool picking;
	/// <summary>Radius of the brush sphere on the fingertip.</summary>
	public float brushRadius = 0.02f;
	public Color32 brushColor = new(230, 60, 50, 255);
	/// <summary>Colors painted with lately, newest first, for both palettes.</summary>
	public List<Color32> recentColors = [];
	public const int RECENT_COLORS = 6;

	/// <summary>Moves a color painted with to the front of the recent colors.</summary>
	public void UsedColor(Color32 color)
	{
		if (recentColors.Count > 0 && recentColors[0].Equals(color)) return;
		recentColors.Remove(color);
		recentColors.Insert(0, color);
		if (recentColors.Count > RECENT_COLORS) recentColors.RemoveAt(RECENT_COLORS);
	}
}
