using System;
using System.Collections.Generic;
using System.Linq;
using StereoKit;

namespace gesh;

/// <summary>A square block of paint pixels for one face, in one page of the
/// atlas. Size is a power of two.</summary>
public readonly record struct GCell(int page, int x, int y, int size)
{
	/// <summary>Side length of the face's triangle in pixels.</summary>
	public float Leg => size - 2 * GPaintAtlas.GUTTER;
}

/// <summary>
/// The paint on the faces: every face has its own square cell of pixels in a
/// texture atlas, so paint never has to be unwrapped when the topology
/// changes. Plain CPU data, see plans/paint.md.
///
/// Cells are sized by the face's size, so paint has about the same texel
/// density everywhere (`texelSize`). They're packed into pages with a buddy
/// allocator: a free block is split in four until it's the size needed, and
/// freed blocks merge back with their three siblings. The renderer only
/// re-uploads pages that changed, so pages are kept small: StereoKit can only
/// upload whole textures.
///
/// A face's triangle covers half its cell: corner a at the cell's top left,
/// b at the top right, c at the bottom left, inset by a gutter so texture
/// filtering doesn't pick up the neighboring cell.
/// </summary>
class GPaintAtlas
{
	public const int PAGE = 512;
	public const int MIN_CELL = 16;
	public const int GUTTER = 2;
	/// <summary>Default size of a paint pixel on the face, in meters (mesh space).</summary>
	/// 3 mm measured as the best trade on the Quest (plans/perf-log.md): 2 mm
	/// made extending big faces take 80 ms instead of 19 ms, and dropped frames
	/// while painting.
	public const float DEFAULT_TEXEL_SIZE = 0.003f;

	public static readonly Color32 BLANK = new(255, 255, 255, 255);

	/// <summary>Size of a paint pixel on the face, in meters (mesh space).
	/// Part of the document, so a file keeps the resolution it was painted at.
	/// Change it with SetTexelSize on the mesh, which refits every face.</summary>
	public float texelSize = DEFAULT_TEXEL_SIZE;

	/// <summary>RGBA pixels, PAGE * PAGE * 4 bytes each, top row first.</summary>
	public readonly List<byte[]> pages = [];
	/// <summary>Bumped whenever a page's pixels change, so the renderer knows
	/// to upload it again.</summary>
	public readonly List<int> pageVersions = [];

	/// <summary>Free blocks by size, as (page, x, y).</summary>
	readonly Dictionary<int, HashSet<(int page, int x, int y)>> free = [];

	/// <summary>Counts every change, so the mesh's content hash notices paint.</summary>
	public int Version { get; private set; }

	/// <summary>Called with (page, x, y, width, height) before any pixels in
	/// that rectangle change, so undo (GHistory) can keep what was there.</summary>
	public Action<int, int, int, int, int>? beforeWrite;

	/// <summary>Undo keeps paint in tiles of this many pixels square.</summary>
	public const int TILE = 32;
	const int TILES_PER_ROW = PAGE / TILE;

	void BeforeWrite(GCell cell) => beforeWrite?.Invoke(cell.page, cell.x, cell.y, cell.size, cell.size);

	/// <summary>The tiles a rectangle of a page touches.</summary>
	public static IEnumerable<int> TilesIn(int x, int y, int width, int height)
	{
		for (int ty = y / TILE; ty <= (y + height - 1) / TILE; ty++)
		{
			for (int tx = x / TILE; tx <= (x + width - 1) / TILE; tx++) yield return ty * TILES_PER_ROW + tx;
		}
	}

	public byte[] ReadTile(int page, int tile)
	{
		byte[] pixels = new byte[TILE * TILE * 4];
		int x = tile % TILES_PER_ROW * TILE, y = tile / TILES_PER_ROW * TILE;
		for (int row = 0; row < TILE; row++) Array.Copy(pages[page], ((y + row) * PAGE + x) * 4, pixels, row * TILE * 4, TILE * 4);
		return pixels;
	}

	/// <summary>Puts a tile back, for undo. Doesn't call beforeWrite.</summary>
	public void WriteTile(int page, int tile, byte[] pixels)
	{
		int x = tile % TILES_PER_ROW * TILE, y = tile / TILES_PER_ROW * TILE;
		for (int row = 0; row < TILE; row++) Array.Copy(pixels, row * TILE * 4, pages[page], ((y + row) * PAGE + x) * 4, TILE * 4);
		pageVersions[page]++;
		Version++;
	}

	/// <summary>Which blocks are free, and the resolution: everything about the
	/// atlas but its pixels.</summary>
	public sealed record AllocatorState(float texelSize, int pageCount, (int size, int page, int x, int y)[] free);

	public AllocatorState SaveAllocator() => new(texelSize, pages.Count, [.. free.SelectMany(kv => kv.Value.Select(b => (kv.Key, b.page, b.x, b.y)))]);

	/// <summary>Back to a saved state. Pages added since stay, as free pages,
	/// since redo may need them again.</summary>
	public void RestoreAllocator(AllocatorState state)
	{
		texelSize = state.texelSize;
		free.Clear();
		foreach (var (size, page, x, y) in state.free) FreeOfSize(size).Add((page, x, y));
		for (int page = state.pageCount; page < pages.Count; page++) FreeOfSize(PAGE).Add((page, 0, 0));
	}

	public IEnumerable<GCell> FreeBlocks => free.SelectMany(kv => kv.Value.Select(b => new GCell(b.page, b.x, b.y, kv.Key)));

	HashSet<(int page, int x, int y)> FreeOfSize(int size)
	{
		if (!free.TryGetValue(size, out var set)) free[size] = set = [];
		return set;
	}

	/// <summary>The cell size for a face, from its two legs (corner a to b and
	/// a to c, which map to the cell's top and left edges).</summary>
	public int SizeFor(Vec3 a, Vec3 b, Vec3 c)
	{
		float pixels = float.Max((b - a).Length, (c - a).Length) / texelSize + 2 * GUTTER;
		int size = MIN_CELL;
		while (size < pixels && size < PAGE) size *= 2;
		return size;
	}

	/// <summary>A cell for a face.</summary>
	/// <param name="blank">Clear it to white. Callers that fill the cell
	/// themselves right away skip it, it's a megabyte for a big cell.</param>
	public GCell Allocate(int size, bool blank = true)
	{
		if (size < MIN_CELL || size > PAGE || (size & (size - 1)) != 0) throw new ArgumentException($"Cell size {size} isn't a power of two from {MIN_CELL} to {PAGE}");

		// The smallest free block that fits, split down to size.
		int blockSize = size;
		while (blockSize <= PAGE && FreeOfSize(blockSize).Count == 0) blockSize *= 2;
		if (blockSize > PAGE)
		{
			pages.Add(new byte[PAGE * PAGE * 4]);
			pageVersions.Add(0);
			blockSize = PAGE;
			FreeOfSize(PAGE).Add((pages.Count - 1, 0, 0));
		}

		var block = FreeOfSize(blockSize).Min();
		FreeOfSize(blockSize).Remove(block);
		while (blockSize > size)
		{
			blockSize /= 2;
			// Keep the top left quarter, free the other three.
			FreeOfSize(blockSize).Add((block.page, block.x + blockSize, block.y));
			FreeOfSize(blockSize).Add((block.page, block.x, block.y + blockSize));
			FreeOfSize(blockSize).Add((block.page, block.x + blockSize, block.y + blockSize));
		}

		GCell cell = new(block.page, block.x, block.y, size);
		if (blank) Fill(cell, BLANK);
		return cell;
	}

	public void Free(GCell cell)
	{
		var block = (cell.page, cell.x, cell.y);
		int size = cell.size;
		if (FreeOfSize(size).Contains(block)) throw new InvalidOperationException($"Freeing {cell} twice");

		// Merge with the three siblings while they're all free.
		while (size < PAGE)
		{
			int parent = size * 2;
			int px = block.x - block.x % parent, py = block.y - block.y % parent;
			(int, int, int)[] siblings = [(block.page, px, py), (block.page, px + size, py), (block.page, px, py + size), (block.page, px + size, py + size)];
			if (!siblings.All(s => s == block || FreeOfSize(size).Contains(s))) break;
			foreach (var s in siblings) FreeOfSize(size).Remove(s);
			block = (block.page, px, py);
			size = parent;
		}
		FreeOfSize(size).Add(block);
	}

	/// <summary>Rebuilds the free blocks after loading: everything not covered
	/// by a used cell is free, in the largest blocks possible.</summary>
	public void SetUsedCells(IReadOnlyCollection<GCell> used, int pageCount)
	{
		while (pages.Count < pageCount)
		{
			pages.Add(new byte[PAGE * PAGE * 4]);
			pageVersions.Add(0);
		}
		free.Clear();
		for (int page = 0; page < pageCount; page++)
		{
			var inPage = used.Where(c => c.page == page).ToList();
			void Visit(int x, int y, int size)
			{
				var inside = inPage.Where(c => c.x >= x && c.y >= y && c.x < x + size && c.y < y + size).ToList();
				if (inside.Count == 0)
				{
					FreeOfSize(size).Add((page, x, y));
					return;
				}
				if (inside.Count == 1 && inside[0].size == size) return;
				if (size == MIN_CELL) throw new InvalidOperationException($"Overlapping cells in page {page} at {x},{y}");
				int half = size / 2;
				Visit(x, y, half);
				Visit(x + half, y, half);
				Visit(x, y + half, half);
				Visit(x + half, y + half, half);
			}
			Visit(0, 0, PAGE);
		}
	}

	/// <summary>Texture coordinates of the face corners a, b, c within the page.</summary>
	public static (Vec2 a, Vec2 b, Vec2 c) CornerUVs(GCell cell)
	{
		Vec2 a = new(cell.x + GUTTER, cell.y + GUTTER);
		return (a / PAGE, (a + new Vec2(cell.Leg, 0)) / PAGE, (a + new Vec2(0, cell.Leg)) / PAGE);
	}

	void Changed(GCell cell)
	{
		pageVersions[cell.page]++;
		Version++;
	}

	public void Fill(GCell cell, Color32 color)
	{
		BeforeWrite(cell);
		byte[] page = pages[cell.page];
		for (int y = cell.y; y < cell.y + cell.size; y++)
		{
			for (int x = cell.x; x < cell.x + cell.size; x++)
			{
				int i = (y * PAGE + x) * 4;
				page[i] = color.r;
				page[i + 1] = color.g;
				page[i + 2] = color.b;
				page[i + 3] = color.a;
			}
		}
		Changed(cell);
	}

	/// <summary>How far past the triangle's edges pixels get painted, so
	/// texture filtering at the edges finds paint, as a fraction of the leg.</summary>
	static float Margin(GCell cell) => GUTTER / cell.Leg;

	/// <summary>Fills a cell from a copy of another cell, where the new
	/// face's (s, t) maps to the old one's as os = s0 + ss*s + st*t,
	/// ot = t0 + ts*s + tt*t. Points off the old triangle use its nearest
	/// edge. Covers the triangle plus the margin, so texture filtering at the
	/// edges finds paint. Big cells are 512x512, so rows are spread over the
	/// CPU cores; each row only writes its own pixels.</summary>
	void Remap(GCell cell, Copy old, float s0, float ss, float st, float t0, float ts, float tt)
	{
		BeforeWrite(cell);
		byte[] page = pages[cell.page];
		float margin = Margin(cell);
		float leg = cell.Leg;
		System.Threading.Tasks.Parallel.For(0, cell.size, py =>
		{
			float t = (py + 0.5f - GUTTER) / leg;
			for (int px = 0; px < cell.size; px++)
			{
				float s = (px + 0.5f - GUTTER) / leg;
				if (s < -margin || t < -margin || s + t > 1 + margin) continue;

				float os = float.Max(s0 + ss * s + st * t, 0), ot = float.Max(t0 + ts * s + tt * t, 0);
				float sum = os + ot;
				if (sum > 1) { os /= sum; ot /= sum; }
				Color32 c = old.Sample(os, ot);
				int i = ((cell.y + py) * PAGE + cell.x + px) * 4;
				page[i] = c.r;
				page[i + 1] = c.g;
				page[i + 2] = c.b;
				page[i + 3] = c.a;
			}
		});
		Changed(cell);
	}

	/// <summary>Paints the part of a face within `radius` of the segment
	/// from-to. The edge is softened over at least a couple of paint pixels,
	/// so strokes are anti-aliased whatever the brush and texel sizes.</summary>
	/// <param name="a">The face's corner positions, in the face's corner order.</param>
	public void PaintCapsule(GCell cell, Vec3 a, Vec3 b, Vec3 c, Vec3 from, Vec3 to, float radius, Color32 color)
	{
		// This runs for every touched face every frame while painting, so it
		// only visits the pixels the brush can reach, with no per-pixel
		// delegate (both measured as big costs on the Quest, plans/perf-log.md).
		Vec3 ab = b - a;
		Vec3 ac = c - a;
		float d00 = Vec3.Dot(ab, ab), d01 = Vec3.Dot(ab, ac), d11 = Vec3.Dot(ac, ac);
		float det = d00 * d11 - d01 * d01;
		// A face with no area shows no paint.
		if (det <= 0) return;

		// Where the segment's ends land on the face, in (s, t), and how far the
		// brush reaches in s and t from there.
		(float s, float t) OnFace(Vec3 p)
		{
			float e0 = Vec3.Dot(ab, p - a), e1 = Vec3.Dot(ac, p - a);
			return ((d11 * e0 - d01 * e1) / det, (d00 * e1 - d01 * e0) / det);
		}
		var (s0, t0) = OnFace(from);
		var (s1, t1) = OnFace(to);
		float reachS = radius * MathF.Sqrt(d11 / det);
		float reachT = radius * MathF.Sqrt(d00 / det);

		float leg = cell.Leg;
		float margin = Margin(cell);
		float sMin = float.Max(float.Min(s0, s1) - reachS, -margin), sMax = float.Min(float.Max(s0, s1) + reachS, 1 + margin);
		float tMin = float.Max(float.Min(t0, t1) - reachT, -margin), tMax = float.Min(float.Max(t0, t1) + reachT, 1 + margin);
		if (sMin > sMax || tMin > tMax) return;
		int pxMin = int.Max((int)MathF.Floor(GUTTER + sMin * leg), 0), pxMax = int.Min((int)MathF.Ceiling(GUTTER + sMax * leg), cell.size - 1);
		int pyMin = int.Max((int)MathF.Floor(GUTTER + tMin * leg), 0), pyMax = int.Min((int)MathF.Ceiling(GUTTER + tMax * leg), cell.size - 1);

		// Anti-aliasing: the soft edge is at least 1.5 paint pixels wide.
		float texel = MathF.Sqrt(float.Max(d00, d11)) / leg;
		float soft = float.Max(radius * 0.1f, texel * 1.5f);

		beforeWrite?.Invoke(cell.page, cell.x + pxMin, cell.y + pyMin, pxMax - pxMin + 1, pyMax - pyMin + 1);
		byte[] page = pages[cell.page];
		bool isPoint = (to - from).LengthSq == 0;
		float alpha = color.a / 255f;
		bool changed = false;
		for (int py = pyMin; py <= pyMax; py++)
		{
			float t = (py + 0.5f - GUTTER) / leg;
			for (int px = pxMin; px <= pxMax; px++)
			{
				float s = (px + 0.5f - GUTTER) / leg;
				if (s < -margin || t < -margin || s + t > 1 + margin) continue;

				Vec3 p = a + ab * s + ac * t;
				float d = isPoint ? (p - from).Length : MathF.Sqrt(Math.DistanceToLineSq(p, from, to));
				float amount = float.Clamp((radius - d) / soft + 0.5f, 0, 1) * alpha;
				if (amount <= 0) continue;

				int i = ((cell.y + py) * PAGE + cell.x + px) * 4;
				Color32 old = new(page[i], page[i + 1], page[i + 2], page[i + 3]);
				Color32 mixed = Blend(old, color, amount);
				if (mixed.r == old.r && mixed.g == old.g && mixed.b == old.b && mixed.a == old.a) continue;
				page[i] = mixed.r;
				page[i + 1] = mixed.g;
				page[i + 2] = mixed.b;
				page[i + 3] = mixed.a;
				changed = true;
			}
		}
		if (changed) Changed(cell);
	}

	/// <summary>A copy of a cell's pixels, with its size.</summary>
	public readonly record struct Copy(byte[] pixels, int size)
	{
		/// <summary>Bilinear sample at (s, t) on the face.</summary>
		public Color32 Sample(float s, float t)
		{
			// Runs per pixel when resizing or resampling big cells, so it's
			// written out plainly.
			int size = this.size;
			byte[] pixels = this.pixels;
			float leg = size - 2 * GUTTER;
			float x = float.Clamp(GUTTER + s * leg - 0.5f, 0, size - 1);
			float y = float.Clamp(GUTTER + t * leg - 0.5f, 0, size - 1);
			int x0 = (int)x, y0 = (int)y;
			int x1 = int.Min(x0 + 1, size - 1), y1 = int.Min(y0 + 1, size - 1);
			float fx = x - x0, fy = y - y0;
			float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
			int i00 = (y0 * size + x0) * 4, i10 = (y0 * size + x1) * 4, i01 = (y1 * size + x0) * 4, i11 = (y1 * size + x1) * 4;
			return new Color32(
				(byte)(pixels[i00] * w00 + pixels[i10] * w10 + pixels[i01] * w01 + pixels[i11] * w11 + 0.5f),
				(byte)(pixels[i00 + 1] * w00 + pixels[i10 + 1] * w10 + pixels[i01 + 1] * w01 + pixels[i11 + 1] * w11 + 0.5f),
				(byte)(pixels[i00 + 2] * w00 + pixels[i10 + 2] * w10 + pixels[i01 + 2] * w01 + pixels[i11 + 2] * w11 + 0.5f),
				(byte)(pixels[i00 + 3] * w00 + pixels[i10 + 3] * w10 + pixels[i01 + 3] * w01 + pixels[i11 + 3] * w11 + 0.5f));
		}
	}

	public Copy CopyCell(GCell cell)
	{
		byte[] page = pages[cell.page];
		byte[] copy = new byte[cell.size * cell.size * 4];
		for (int y = 0; y < cell.size; y++) Array.Copy(page, ((cell.y + y) * PAGE + cell.x) * 4, copy, y * cell.size * 4, cell.size * 4);
		return new Copy(copy, cell.size);
	}

	/// <summary>Paints a new face with the paint of an old face it was cut out
	/// of: each pixel takes the old face's paint at the same point in space,
	/// projected onto the old face.</summary>
	public void Resample(GCell cell, Vec3 a, Vec3 b, Vec3 c, Copy old, Vec3 oldA, Vec3 oldB, Vec3 oldC)
	{
		// Projecting onto the old face's plane is linear, so three points give
		// the whole mapping.
		var (s0, t0) = OnTrianglePlane(a, oldA, oldB, oldC);
		var (s1, t1) = OnTrianglePlane(b, oldA, oldB, oldC);
		var (s2, t2) = OnTrianglePlane(c, oldA, oldB, oldC);
		Remap(cell, old, s0, s1 - s0, s2 - s0, t0, t1 - t0, t2 - t0);
	}

	/// <summary>The same face's paint at a different cell size.</summary>
	public void Rescale(GCell cell, Copy old) => Remap(cell, old, 0, 1, 0, 0, 0, 1);

	/// <summary>Swaps the cell's two texture directions, see OrientFaceToward.</summary>
	public void Transpose(GCell cell)
	{
		BeforeWrite(cell);
		byte[] page = pages[cell.page];
		for (int y = 0; y < cell.size; y++)
		{
			for (int x = y + 1; x < cell.size; x++)
			{
				int i = ((cell.y + y) * PAGE + cell.x + x) * 4;
				int j = ((cell.y + x) * PAGE + cell.x + y) * 4;
				for (int k = 0; k < 4; k++) (page[i + k], page[j + k]) = (page[j + k], page[i + k]);
			}
		}
		Changed(cell);
	}

	/// <summary>The paint color at (s, t) on a face, for the color picker.</summary>
	public Color32 ColorAt(GCell cell, float s, float t)
	{
		int x = int.Clamp((int)(GUTTER + s * cell.Leg), 0, cell.size - 1);
		int y = int.Clamp((int)(GUTTER + t * cell.Leg), 0, cell.size - 1);
		int i = ((cell.y + y) * PAGE + cell.x + x) * 4;
		byte[] page = pages[cell.page];
		return new Color32(page[i], page[i + 1], page[i + 2], page[i + 3]);
	}

	/// <summary>Where a point is on a triangle, as s along a->b and t along
	/// a->c, after projecting it onto the triangle and clamping it inside.</summary>
	public static (float s, float t) PlaneCoordinates(Vec3 p, Vec3 a, Vec3 b, Vec3 c) => OnTrianglePlane(Math.ClosestPointToTriangle(p, a, b, c), a, b, c);

	/// <summary>Where a point projects onto a triangle's plane, as s along
	/// a->b and t along a->c, not clamped to the triangle.</summary>
	static (float s, float t) OnTrianglePlane(Vec3 p, Vec3 a, Vec3 b, Vec3 c)
	{
		Vec3 v0 = b - a, v1 = c - a, v2 = p - a;
		float d00 = Vec3.Dot(v0, v0), d01 = Vec3.Dot(v0, v1), d11 = Vec3.Dot(v1, v1);
		float d20 = Vec3.Dot(v2, v0), d21 = Vec3.Dot(v2, v1);
		float denom = d00 * d11 - d01 * d01;
		if (denom == 0) throw new InvalidOperationException($"Degenerate triangle {a} {b} {c}");
		return ((d11 * d20 - d01 * d21) / denom, (d00 * d21 - d01 * d20) / denom);
	}

	static Color32 Blend(Color32 from, Color32 to, float amount)
	{
		if (amount <= 0) return from;
		static byte Mix(byte x, byte y, float t) => (byte)(x + (y - x) * t + 0.5f);
		return new Color32(Mix(from.r, to.r, amount), Mix(from.g, to.g, amount), Mix(from.b, to.b, amount), 255);
	}

	/// <summary>A copy of a page, for saving.</summary>
	public byte[] PagePixels(int page) => (byte[])pages[page].Clone();

	public void SetPagePixels(int page, byte[] rgba)
	{
		if (rgba.Length != PAGE * PAGE * 4) throw new ArgumentException($"Page must be {PAGE}x{PAGE} RGBA");
		beforeWrite?.Invoke(page, 0, 0, PAGE, PAGE);
		Array.Copy(rgba, pages[page], rgba.Length);
		pageVersions[page]++;
		Version++;
	}
}
