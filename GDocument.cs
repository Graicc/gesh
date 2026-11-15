using System.Linq;
using StereoKit;

namespace gesh;

/// <summary>What's being worked on: the mesh, where it sits in the world, and
/// the file it belongs to. Saving and loading swap the whole thing.</summary>
class GDocument
{
	public readonly GEditableMesh mesh;
	/// <summary>Undo and redo for this document's mesh.</summary>
	public readonly GHistory history;
	/// <summary>Mesh space to world space, moved around by air pinching.</summary>
	public GWorld world;
	/// <summary>File name without folder or extension.</summary>
	public string name;
	/// <summary>Content hash of the mesh when it was last saved or loaded, to
	/// tell whether there are unsaved changes without a dirty flag.</summary>
	public int savedHash;

	public GDocument(GEditableMesh mesh, GWorld world, string name)
	{
		this.mesh = mesh;
		this.world = world;
		this.name = name;
		savedHash = mesh.ContentHash();
		history = new GHistory(mesh);
	}

	public bool HasUnsavedChanges => mesh.ContentHash() != savedHash;

	public static GDocument New() => new(StartingMesh(), GWorld.Identity, GFiles.DefaultName());

	/// <summary>The first launch starts with a small tetrahedron, about two
	/// baseballs across, so there are corners, edges and faces to try.</summary>
	public static GDocument FirstLaunch() => new(Tetrahedron(new Vec3(0, -0.2f, -0.4f), 0.296f), GWorld.Identity, GFiles.DefaultName());

	static GEditableMesh Tetrahedron(Vec3 center, float diameter)
	{
		GEditableMesh mesh = new();
		Vec3[] corners = [new(1, 1, 1), new(1, -1, -1), new(-1, 1, -1), new(-1, -1, 1)];
		foreach (Vec3 c in corners) mesh.AddVertex(new Vertex(center + c.Normalized * (diameter / 2), Vec3.Up));
		for (int i = 0; i < 4; i++)
		{
			for (int j = i + 1; j < 4; j++) mesh.AddEdge(new GEdge(i, j));
		}
		// Each face is the three corners other than one, wound to face out.
		for (int skip = 0; skip < 4; skip++)
		{
			int[] f = Enumerable.Range(0, 4).Where(i => i != skip).ToArray();
			Vec3 a = corners[f[0]], b = corners[f[1]], c = corners[f[2]];
			Vec3 normal = Vec3.Cross(b - a, c - a);
			bool outward = Vec3.Dot(normal, (a + b + c) / 3) > 0;
			mesh.AddFace(outward ? new GFace(f[0], f[1], f[2]) : new GFace(f[0], f[2], f[1]));
		}
		return mesh;
	}

	/// <summary>One triangle facing the user, to pull everything else out of.</summary>
	public static GEditableMesh StartingMesh()
	{
		GEditableMesh mesh = new();
		mesh.AddVertex(new Vertex(new Vec3(0, -0.5f, -0.5f), Vec3.Up));
		mesh.AddVertex(new Vertex(new Vec3(0, 0.5f, -0.5f), Vec3.Up));
		mesh.AddVertex(new Vertex(new Vec3(1.0f, 0f, -0.5f), Vec3.Up));
		mesh.AddEdge(new GEdge(0, 1));
		mesh.AddEdge(new GEdge(1, 2));
		mesh.AddEdge(new GEdge(2, 0));
		mesh.AddFace(new GFace(2, 1, 0));
		return mesh;
	}
}
