using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using SharpGLTF.Schema2;
using StereoKit;

namespace gesh;

record GFileEntry(string name, string path, DateTime modified);

/// <summary>Saving and loading meshes as .glb files. See plans/save.md.</summary>
static partial class GFiles
{
	public const string AUTOSAVE_NAME = "autosave";
	/// <summary>2 added paint, 3 sized paint cells by face size. Paint from
	/// version 2 isn't read, those faces load blank.</summary>
	const int VERSION = 3;

	/// <summary>Where saves live, from the platform specific GFiles file.
	/// Created if it doesn't exist yet.</summary>
	public static string Folder()
	{
		string folder = PlatformFolder();
		Directory.CreateDirectory(folder);
		return folder;
	}

	/// <summary>Where saves live, without creating it.</summary>
	public static string FolderPath() => PlatformFolder();

	public static string PathFor(string name) => Path.Combine(Folder(), SanitizeName(name) + ".glb");

	public static string DefaultName() => $"gesh_{DateTime.Now:yyyy-MM-dd_HHmm}";

	/// <summary>Drops characters that can't be in a file name. Never empty.</summary>
	public static string SanitizeName(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		string clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
		return clean.Length == 0 ? "untitled" : clean;
	}

	/// <summary>Saved files, newest first.</summary>
	public static List<GFileEntry> List()
	{
		return new DirectoryInfo(Folder()).GetFiles("*.glb")
			.OrderByDescending(f => f.LastWriteTimeUtc)
			.Select(f => new GFileEntry(Path.GetFileNameWithoutExtension(f.Name), f.FullName, f.LastWriteTimeUtc))
			.ToList();
	}

	#region saving

	/// <summary>Writes the mesh as a glb. Everything gesh needs to reopen it
	/// exactly is in extras.gesh; the triangles and lines are for other programs.</summary>
	/// <param name="thumbnailPng">Embedded as an image no material uses.</param>
	public static void Save(string path, GDocument doc, byte[]? thumbnailPng)
	{
		ModelRoot model = ToGltf(doc.mesh, doc.world, doc.name, thumbnailPng);

		// Write next to it and swap, so a crash mid-write can't destroy the old file.
		string temp = path + ".tmp";
		model.SaveGLB(temp);
		File.Move(temp, path, overwrite: true);
	}

	static ModelRoot ToGltf(GEditableMesh mesh, GWorld world, string name, byte[]? thumbnailPng)
	{
		// Removed slots are left out, so indices are remapped to be compact.
		int[] alive = mesh.vertices.Indices().ToArray();
		Dictionary<int, int> remap = new();
		for (int i = 0; i < alive.Length; i++) remap[alive[i]] = i;

		Vector3[] positions = alive.Select(i => mesh.vertices.values[i].pos.v).ToArray();
		int[] faceIndices = mesh.faces.Items().SelectMany(f => new[] { remap[f.a], remap[f.b], remap[f.c] }).ToArray();
		int[] edgeIndices = mesh.edges.Items().SelectMany(e => new[] { remap[e.a], remap[e.b] }).ToArray();
		// Edges that are a side of some face would import as duplicate loose
		// edges in other programs, so only loose ones are written as lines.
		int[] looseEdgeIndices = mesh.edges.Items()
			.Where(e => !mesh.faces.Items().Any(f => f.Contains(e)))
			.SelectMany(e => new[] { remap[e.a], remap[e.b] }).ToArray();
		int[] loneVertexIndices = alive
			.Where(v => !mesh.edges.Items().Any(e => e.Contains(v)))
			.Select(v => remap[v]).ToArray();

		ModelRoot model = ModelRoot.CreateModel();
		// Image index of each paint page, -1 for pages no face uses.
		int[] pageImages = Enumerable.Repeat(-1, mesh.paint.pages.Count).ToArray();
		Scene scene = model.UseScene("gesh");

		if (positions.Length > 0)
		{
			SharpGLTF.Schema2.Mesh gltfMesh = model.CreateMesh("gesh");
			SharpGLTF.Schema2.Material material = model.CreateMaterial("gesh")
				.WithPBRMetallicRoughness(new Vector4(1, 1, 1, 1), null, null, 0, 1);

			// Faces, one primitive per paint page so each can have its page as
			// its texture. Each face gets its own three corners, since a vertex
			// has different texture coordinates in each face's cell.
			foreach (var pageFaces in mesh.faces.Indices().GroupBy(f => mesh.CellOf(f).page))
			{
				List<Vector3> cornerPositions = [];
				List<Vector2> cornerUVs = [];
				foreach (int f in pageFaces)
				{
					GFace face = mesh.faces.values[f];
					var (uvA, uvB, uvC) = GPaintAtlas.CornerUVs(mesh.CellOf(f));
					cornerPositions.AddRange([mesh.vertices.values[face.a].pos.v, mesh.vertices.values[face.b].pos.v, mesh.vertices.values[face.c].pos.v]);
					cornerUVs.AddRange([uvA.v, uvB.v, uvC.v]);
				}

				Image pageImage = model.CreateImage($"paint{pageFaces.Key}");
				pageImage.Content = new SharpGLTF.Memory.MemoryImage(GPng.Encode(mesh.paint.PagePixels(pageFaces.Key), GPaintAtlas.PAGE, GPaintAtlas.PAGE));
				pageImages[pageFaces.Key] = pageImage.LogicalIndex;
				SharpGLTF.Schema2.Material pageMaterial = model.CreateMaterial($"paint{pageFaces.Key}").WithPBRMetallicRoughness();
				pageMaterial.FindChannel("BaseColor")!.Value.SetTexture(0, pageImage, null,
					TextureWrapMode.CLAMP_TO_EDGE, TextureWrapMode.CLAMP_TO_EDGE,
					TextureMipMapFilter.LINEAR, TextureInterpolationFilter.LINEAR);

				gltfMesh.CreatePrimitive()
					.WithVertexAccessor("POSITION", cornerPositions)
					.WithVertexAccessor("TEXCOORD_0", cornerUVs)
					.WithIndicesAccessor(PrimitiveType.TRIANGLES, Enumerable.Range(0, cornerPositions.Count).ToArray())
					.WithMaterial(pageMaterial);
			}
			if (looseEdgeIndices.Length > 0)
			{
				gltfMesh.CreatePrimitive()
					.WithVertexAccessor("POSITION", positions)
					.WithIndicesAccessor(PrimitiveType.LINES, looseEdgeIndices)
					.WithMaterial(material);
			}
			if (loneVertexIndices.Length > 0)
			{
				gltfMesh.CreatePrimitive()
					.WithVertexAccessor("POSITION", positions)
					.WithIndicesAccessor(PrimitiveType.POINTS, loneVertexIndices);
			}

			scene.CreateNode("gesh").WithMesh(gltfMesh);
		}

		JsonObject gesh = new()
		{
			["version"] = VERSION,
			// The document's name, so an autosave reopens under the real name.
			["name"] = name,
			["positions"] = new JsonArray(positions.SelectMany(p => new[] { p.X, p.Y, p.Z }).Select(f => (JsonNode)f).ToArray()),
			["edges"] = new JsonArray(edgeIndices.Select(i => (JsonNode)i).ToArray()),
			["faces"] = new JsonArray(faceIndices.Select(i => (JsonNode)i).ToArray()),
			// Each face's paint cell as page, x, y, size.
			["faceCells"] = new JsonArray(mesh.faces.Indices().Select(f => mesh.CellOf(f)).SelectMany(c => new[] { c.page, c.x, c.y, c.size }).Select(i => (JsonNode)i).ToArray()),
			["pageCount"] = mesh.paint.pages.Count,
			["texelSize"] = mesh.paint.texelSize,
			["pages"] = new JsonArray(pageImages.Select(i => (JsonNode)i).ToArray()),
			["world"] = new JsonArray(
				world.position.x, world.position.y, world.position.z,
				world.rotation.x, world.rotation.y, world.rotation.z, world.rotation.w,
				world.scale),
		};

		if (thumbnailPng != null)
		{
			Image image = model.CreateImage("thumbnail");
			image.Content = new SharpGLTF.Memory.MemoryImage(thumbnailPng);
			gesh["thumbnail"] = image.LogicalIndex;
		}

		model.Extras = new JsonObject { ["gesh"] = gesh };
		return model;
	}

	#endregion

	#region loading

	public static GDocument Load(string path)
	{
		ModelRoot model = ModelRoot.Load(path);
		JsonObject? gesh = (model.Extras as JsonObject)?["gesh"] as JsonObject;
		string fileName = Path.GetFileNameWithoutExtension(path);
		if (gesh == null) return new GDocument(FromForeignGltf(model), GWorld.Identity, fileName);

		var (mesh, world) = FromGeshExtras(gesh, model);
		return new GDocument(mesh, world, (string?)gesh["name"] ?? fileName);
	}

	/// <summary>Our own files: extras.gesh has the exact editor state.</summary>
	static (GEditableMesh, GWorld) FromGeshExtras(JsonObject gesh, ModelRoot model)
	{
		int version = (int)gesh["version"]!;
		if (version > VERSION) throw new InvalidDataException($"File is from a newer gesh (version {version})");

		float[] p = gesh["positions"]!.AsArray().Select(n => (float)n!).ToArray();
		int[] edges = gesh["edges"]!.AsArray().Select(n => (int)n!).ToArray();
		int[] faces = gesh["faces"]!.AsArray().Select(n => (int)n!).ToArray();
		float[] w = gesh["world"]!.AsArray().Select(n => (float)n!).ToArray();

		GEditableMesh mesh = new();
		for (int i = 0; i < p.Length; i += 3) mesh.AddVertex(new Vertex(new Vec3(p[i], p[i + 1], p[i + 2]), Vec3.Up));
		for (int i = 0; i < edges.Length; i += 2) mesh.AddEdge(new GEdge(edges[i], edges[i + 1]));

		// Files before version 3 load with blank paint.
		GCell[]? faceCells = null;
		if (version >= 3)
		{
			int[] c = gesh["faceCells"]!.AsArray().Select(n => (int)n!).ToArray();
			faceCells = Enumerable.Range(0, c.Length / 4).Select(i => new GCell(c[i * 4], c[i * 4 + 1], c[i * 4 + 2], c[i * 4 + 3])).ToArray();
			mesh.paint.texelSize = (float)gesh["texelSize"]!;
			mesh.paint.SetUsedCells(faceCells, (int)gesh["pageCount"]!);
			int[] pages = gesh["pages"]!.AsArray().Select(n => (int)n!).ToArray();
			for (int page = 0; page < pages.Length; page++)
			{
				if (pages[page] < 0) continue;
				var (rgba, width, height) = GPng.Decode(model.LogicalImages[pages[page]].Content.Content.ToArray());
				if (width != GPaintAtlas.PAGE || height != GPaintAtlas.PAGE) throw new InvalidDataException($"Paint page is {width}x{height}");
				mesh.paint.SetPagePixels(page, rgba);
			}
		}
		for (int i = 0; i < faces.Length; i += 3) mesh.AddFace(new GFace(faces[i], faces[i + 1], faces[i + 2]), faceCells?[i / 3]);

		GWorld world = new(new Vec3(w[0], w[1], w[2]), new Quat(w[3], w[4], w[5], w[6]), w[7]);
		return (mesh, world);
	}

	/// <summary>A glb from another program. Its data doesn't follow our rules,
	/// so this cleans it up: vertices on the same spot are welded, triangle
	/// sides become edges, and degenerate or duplicate triangles are dropped.</summary>
	static GEditableMesh FromForeignGltf(ModelRoot model)
	{
		GEditableMesh mesh = new();
		Dictionary<Vector3, int> welded = new();
		int Weld(Vector3 p)
		{
			if (!welded.TryGetValue(p, out int index))
			{
				index = mesh.AddVertex(new Vertex(new Vec3(p.X, p.Y, p.Z), Vec3.Up));
				welded[p] = index;
			}
			return index;
		}
		void Edge(int a, int b)
		{
			if (a != b && !mesh.GetEdgeBetween(a, b).HasValue) mesh.AddEdge(new GEdge(a, b));
		}

		foreach (Node node in model.LogicalNodes.Where(n => n.Mesh != null))
		{
			Matrix4x4 transform = node.WorldMatrix;
			foreach (MeshPrimitive prim in node.Mesh.Primitives)
			{
				var positionAccessor = prim.GetVertexAccessor("POSITION");
				if (positionAccessor == null) continue;
				int[] v = positionAccessor.AsVector3Array().Select(p => Weld(Vector3.Transform(p, transform))).ToArray();

				foreach (var (a, b, c) in prim.GetTriangleIndices())
				{
					GFace face = new(v[a], v[b], v[c]);
					if (face.a == face.b || face.b == face.c || face.c == face.a) continue;
					if (mesh.GetFaceBetween(face).HasValue) continue;
					foreach (GEdge side in face.Edges()) Edge(side.a, side.b);
					mesh.AddFace(face);
				}
				foreach (var (a, b) in prim.GetLineIndices()) Edge(v[a], v[b]);
			}
		}
		return mesh;
	}

	/// <summary>The thumbnail PNG inside a file we saved, or null.</summary>
	public static byte[]? ReadThumbnail(string path)
	{
		ModelRoot model = ModelRoot.Load(path);
		JsonObject? gesh = (model.Extras as JsonObject)?["gesh"] as JsonObject;
		if (gesh?["thumbnail"] is not JsonNode index) return null;
		return model.LogicalImages[(int)index].Content.Content.ToArray();
	}

	#endregion
}
