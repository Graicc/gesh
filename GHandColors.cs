using StereoKit;

namespace gesh;

/// <summary>
/// Colors the hands by tool: red all over for Delete, the selection's blue
/// for Select, the paint color on the index finger for Brush and Fill, plain
/// otherwise.
///
/// StereoKit's generated hand takes its color from one small texture with a
/// column per finger, thumb first: it already maps each finger to the center
/// of its column (input_hand.cpp: u = (finger + 0.5) / 5), with v running
/// from the fingertip down to the wrist. Since u sits on pixel centers, and
/// each finger is its own tube, filtering never mixes fingers. Meta's hand
/// mesh is one skin, where a triangle can span fingers, so it takes Hand and
/// Index directly and blends by skin weight, see GMetaHandMesh.
/// </summary>
class GHandColors
{
	public const int FINGERS = 5;
	public const int INDEX = 1;
	const int ROWS = 16;

	/// <summary>Down the finger: StereoKit's own hand gradient
	/// (input_render.cpp), brightness and alpha, fingertip first.</summary>
	static readonly Gradient FADE = new(
		new GradientKey(new Color(.4f, .4f, .4f, 0), 0),
		new GradientKey(new Color(.6f, .6f, .6f, 0), 0.55f),
		new GradientKey(new Color(.8f, .8f, .8f, 1), 0.7f),
		new GradientKey(new Color(1, 1, 1, 1), 1));

	readonly Color32[] pixels = new Color32[FINGERS * ROWS];
	readonly Color[] tints = new Color[FINGERS];
	public readonly Tex texture = new(TexType.ImageNomips, TexFormat.Rgba32Linear) { AddressMode = TexAddress.Clamp };

	/// <summary>Takes over StereoKit's hand material, so call after
	/// SK.Initialize.</summary>
	public GHandColors()
	{
		Material hand = Default.MaterialHand.Copy();
		hand[MatParamName.DiffuseTex] = texture;
		Input.HandMaterial(Handed.Max, hand);
		System.Array.Fill(tints, Color.White);
		Upload();
	}

	/// <summary>This frame's color for the hand, and for its index finger,
	/// linear. Meta's hand mesh takes them directly, see GMetaHandMesh.</summary>
	public Color Hand { get; private set; } = Color.White;
	public Color Index { get; private set; } = Color.White;

	/// <summary>Sets this frame's colors, uploading only when they change.</summary>
	public void Tick(GSettings settings)
	{
		Color all = settings.tool switch
		{
			GTool.Delete => GTheme.DANGER.ToLinear(),
			// As the selected vertices show it: their vertex colors take the
			// theme's values as they are.
			GTool.Select => GTheme.ACCENT,
			_ => Color.White,
		};
		Color index = settings.tool is GTool.Brush or GTool.Fill && !settings.picking ? Gamma(settings.brushColor).ToLinear() : all;
		Hand = all;
		Index = index;
		bool changed = false;
		for (int f = 0; f < FINGERS; f++)
		{
			Color tint = f == INDEX ? index : all;
			changed |= !tints[f].Equals(tint);
			tints[f] = tint;
		}
		if (changed) Upload();
	}

	static Color Gamma(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f);

	void Upload()
	{
		for (int y = 0; y < ROWS; y++)
		{
			Color fade = FADE.Get(1 - y / (ROWS - 1f));
			for (int f = 0; f < FINGERS; f++)
				pixels[f + y * FINGERS] = new Color(fade.r * tints[f].r, fade.g * tints[f].g, fade.b * tints[f].b, fade.a).ToColor32Sat();
		}
		texture.SetColors(FINGERS, ROWS, pixels);
	}
}
