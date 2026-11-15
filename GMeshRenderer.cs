using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using StereoKit;

namespace gesh;

/// <summary>Draws a GEditableMesh: faces from both sides, vertex handles, and
/// edges as cylinders. Owns the GPU side, so the mesh itself stays plain data.</summary>
class GMeshRenderer
{
	/// <summary>The faces whose paint is on one atlas page, drawn with that
	/// page as their texture.</summary>
	class Page
	{
		public Mesh mesh = new();
		public Tex tex = new(TexType.ImageNomips | TexType.Dynamic, TexFormat.Rgba32);
		public Material material;
		/// <summary>The atlas page version last uploaded to tex.</summary>
		public int uploadedVersion = -1;
		public Vertex[] verts = [];
		public uint[] inds = [];
		public int count;

		public Page(Material front)
		{
			tex.AddressMode = TexAddress.Clamp;
			material = front.Copy();
			material[MatParamName.DiffuseTex] = tex;
		}
	}

	List<Page> pages = [];
	Material frontMaterial = new("mesh.hlsl");
	// Back faces won't show up in exported meshes, so draw them as a see-through
	// red grid instead of hiding them.
	Material backMaterial = new("mesh_back.hlsl")
	{
		FaceCull = Cull.Front,
		Transparency = Transparency.Blend,
		DepthWrite = false,
	};

	Mesh edgeMesh = new();
	Material edgeMaterial = new("edge.hlsl") { FaceCull = Cull.None };
	Mesh vertexMesh = new();
	Material vertexMaterial = new("vertex.hlsl") { FaceCull = Cull.None };

	// Reused every frame for uploading to the GPU, only grown when too small.
	Vertex[] edgeVerts = [];
	uint[] edgeInds = [];
	Vertex[] vertexVerts = [];
	uint[] vertexInds = [];

	/// <summary>Selected vertices and edges are the accent blue, see
	/// plans/multiselect.md, and selected faces get a light tint of it.</summary>
	static readonly Color32 SELECTED = GTheme.ACCENT.ToColor32Sat();
	static readonly Color32 SELECTED_FACE = new(190, 220, 255, 255);

	static void EnsureSize<T>(ref T[] array, int size)
	{
		if (array.Length < size) Array.Resize(ref array, int.Max(size, array.Length * 2));
	}

	/// <summary>Fills each page's mesh with its faces, three corners per face
	/// since a vertex has different texture coordinates in each face's cell,
	/// and uploads paint pages that changed.</summary>
	void UpdateFaces(GEditableMesh source)
	{
		GPaintAtlas paint = source.paint;
		while (pages.Count < paint.pages.Count) pages.Add(new Page(frontMaterial));
		foreach (Page page in pages) page.count = 0;

		Color32 white = new(255, 255, 255, 255);
		ReadOnlySpan<Vertex> vertices = CollectionsMarshal.AsSpan(source.vertices.values);
		foreach (int f in source.faces.Indices())
		{
			GFace face = source.faces.values[f];
			// Selected faces get a blue tint over their paint.
			bool selected = source.selected.Count > 0 && source.selected.Contains(face.a) && source.selected.Contains(face.b) && source.selected.Contains(face.c);
			Color32 tint = selected ? SELECTED_FACE : white;
			GCell cell = source.CellOf(f);
			Page page = pages[cell.page];
			EnsureSize(ref page.verts, page.count + 3);
			EnsureSize(ref page.inds, page.count + 3);

			var (uvA, uvB, uvC) = GPaintAtlas.CornerUVs(cell);
			page.verts[page.count] = new Vertex(vertices[face.a].pos, Vec3.Up, uvA, tint);
			page.verts[page.count + 1] = new Vertex(vertices[face.b].pos, Vec3.Up, uvB, tint);
			page.verts[page.count + 2] = new Vertex(vertices[face.c].pos, Vec3.Up, uvC, tint);
			for (int i = 0; i < 3; i++) page.inds[page.count + i] = (uint)(page.count + i);
			page.count += 3;
		}

		for (int i = 0; i < pages.Count; i++)
		{
			Page page = pages[i];
			// StereoKit ignores empty data, so empty pages just aren't drawn.
			if (page.count > 0) page.mesh.SetData(page.verts, page.count, page.inds, page.count);
			if (page.uploadedVersion != paint.pageVersions[i])
			{
				page.tex.SetColors(GPaintAtlas.PAGE, GPaintAtlas.PAGE, paint.pages[i], TexFormat.Rgba32);
				page.uploadedVersion = paint.pageVersions[i];
			}
		}
	}

	/// <summary>One camera facing quad per edge, edge.hlsl turns them into
	/// cylinders.</summary>
	/// <param name="radius">Edge radius in mesh space, for the bounds.</param>
	/// <returns>False if there are no edges to draw.</returns>
	bool UpdateEdgeMesh(GEditableMesh source, float radius)
	{
		Color32 black = new(0, 0, 0, 255);
		EnsureSize(ref edgeVerts, source.edges.values.Count * 4);
		EnsureSize(ref edgeInds, source.edges.values.Count * 6);

		int vertCount = 0;
		int indCount = 0;
		Vec3 min = new(float.MaxValue);
		Vec3 max = new(float.MinValue);
		ReadOnlySpan<Vertex> vertices = CollectionsMarshal.AsSpan(source.vertices.values);
		foreach (GEdge e in source.edges.Items())
		{
			Vec3 a = vertices[e.a].pos;
			Vec3 b = vertices[e.b].pos;
			Color32 color = source.selected.Count > 0 && source.selected.Contains(e.a) && source.selected.Contains(e.b) ? SELECTED : black;
			min = Vec3.Min(min, Vec3.Min(a, b));
			max = Vec3.Max(max, Vec3.Max(a, b));

			uint start = (uint)vertCount;
			edgeVerts[vertCount++] = new Vertex(a, b, new Vec2(-1, 0), color);
			edgeVerts[vertCount++] = new Vertex(a, b, new Vec2(1, 0), color);
			edgeVerts[vertCount++] = new Vertex(a, b, new Vec2(-1, 1), color);
			edgeVerts[vertCount++] = new Vertex(a, b, new Vec2(1, 1), color);
			edgeInds[indCount++] = start;
			edgeInds[indCount++] = start + 2;
			edgeInds[indCount++] = start + 1;
			edgeInds[indCount++] = start + 1;
			edgeInds[indCount++] = start + 2;
			edgeInds[indCount++] = start + 3;
		}
		if (indCount == 0) return false;

		// The edge's end point is stored in the normal, so StereoKit's own
		// bounds would only cover the start points, and culling would hide
		// edges that are still on screen.
		edgeMesh.SetData(edgeVerts, vertCount, edgeInds, indCount, calculateBounds: false);
		edgeMesh.Bounds = Bounds.FromCorners(min - new Vec3(radius), max + new Vec3(radius));
		return true;
	}

	/// <summary>One camera facing quad per vertex, vertex.hlsl turns them into
	/// spheres.</summary>
	/// <param name="radius">Sphere radius in mesh space, for the bounds.</param>
	/// <returns>False if there are no vertices to draw.</returns>
	bool UpdateVertexMesh(GEditableMesh source, float radius)
	{
		// Black like the edges, so where they meet reads as one shape.
		Color32 black = new(0, 0, 0, 255);
		EnsureSize(ref vertexVerts, source.vertices.values.Count * 4);
		EnsureSize(ref vertexInds, source.vertices.values.Count * 6);

		int vertCount = 0;
		int indCount = 0;
		ReadOnlySpan<Vertex> vertices = CollectionsMarshal.AsSpan(source.vertices.values);
		foreach (int v in source.vertices.Indices())
		{
			Vec3 p = vertices[v].pos;
			Color32 color = source.selected.Contains(v) ? SELECTED : black;
			uint start = (uint)vertCount;
			vertexVerts[vertCount++] = new Vertex(p, Vec3.Up, new Vec2(-1, -1), color);
			vertexVerts[vertCount++] = new Vertex(p, Vec3.Up, new Vec2(1, -1), color);
			vertexVerts[vertCount++] = new Vertex(p, Vec3.Up, new Vec2(-1, 1), color);
			vertexVerts[vertCount++] = new Vertex(p, Vec3.Up, new Vec2(1, 1), color);
			vertexInds[indCount++] = start;
			vertexInds[indCount++] = start + 2;
			vertexInds[indCount++] = start + 1;
			vertexInds[indCount++] = start + 1;
			vertexInds[indCount++] = start + 2;
			vertexInds[indCount++] = start + 3;
		}
		if (indCount == 0) return false;

		// The positions are the sphere centers, the bounds need the radius too.
		vertexMesh.SetData(vertexVerts, vertCount, vertexInds, indCount);
		Bounds centers = vertexMesh.Bounds;
		vertexMesh.Bounds = new Bounds(centers.center, centers.dimensions + new Vec3(radius * 2));
		return true;
	}

	/// <param name="world">Mesh space to world space.</param>
	public void Draw(GEditableMesh source, Matrix world, GSettings settings)
	{
		UpdateFaces(source);
		foreach (Page page in pages)
		{
			if (page.count == 0) continue;
			page.mesh.Draw(page.material, world, Color.White, GSaver.SCENE_LAYER);
			page.mesh.Draw(backMaterial, world, Color.White, GSaver.SCENE_LAYER);
		}

		// The outline is for editing; painting shows the paint unobstructed.
		if (!settings.meshOutline || settings.tool is GTool.Brush or GTool.Fill) return;
		// Handles are sized in mesh space, so they scale with the world. The
		// shaders' radius is in world space.
		float vertexRadius = settings.VertexDrawSize / 2;
		if (UpdateVertexMesh(source, vertexRadius))
		{
			vertexMaterial["radius"] = vertexRadius * world.Scale.x;
			vertexMesh.Draw(vertexMaterial, world, Color.White, GSaver.SCENE_LAYER);
		}

		float radius = settings.EdgeDrawThickness / 2;
		if (UpdateEdgeMesh(source, radius))
		{
			edgeMaterial["radius"] = radius * world.Scale.x;
			edgeMesh.Draw(edgeMaterial, world, Color.White, GSaver.SCENE_LAYER);
		}
	}
}
