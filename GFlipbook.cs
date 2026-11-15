using StereoKit;

namespace gesh;

/// <summary>
/// A looping animation from a sheet of frames in a grid, left to right then
/// top to bottom, as scripts/tutorial.py writes them. Drawn in the current
/// UI layout like an icon, showing one frame by moving the texture's window
/// over the sheet.
/// </summary>
class GFlipbook
{
	const int COLUMNS = 9;
	const float FPS = 24;

	readonly int frames;
	readonly int rows;
	readonly Tex tex;
	readonly Material material;

	/// <param name="file">The sheet, under Assets.</param>
	/// <param name="frames">How many frames it holds; scripts/tutorial.py
	/// prints them.</param>
	public GFlipbook(string file, int frames)
	{
		this.frames = frames;
		rows = (frames + COLUMNS - 1) / COLUMNS;
		tex = Tex.FromFile(file);
		tex.AddressMode = TexAddress.Clamp;
		material = Material.Unlit.Copy();
		material[MatParamName.DiffuseTex] = tex;
		material.Transparency = Transparency.Blend;
		material.DepthWrite = false;
		material.QueueOffset = 1;
	}

	/// <summary>The frame at `seconds` into the loop, in a square of the
	/// layout `size` wide, centered. Until the sheet has loaded, just the
	/// space for it, rather than a white square.</summary>
	public void Draw(float size, double seconds)
	{
		Bounds b = UI.LayoutReserve(new Vec2(UI.LayoutRemaining.x, size));
		if (tex.AssetState != AssetState.Loaded) return;
		int frame = (int)(seconds * FPS) % frames;
		material["tex_trans"] = new Vec4((float)(frame % COLUMNS) / COLUMNS, (float)(frame / COLUMNS) / rows, 1f / COLUMNS, 1f / rows);
		Vec3 at = b.center + new Vec3(0, 0, -GDraw.LIFT);
		Mesh.Quad.Draw(material, Matrix.TS(at, new Vec3(size, size, 1)));
	}
}
