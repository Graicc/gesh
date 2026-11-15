using StereoKit;

namespace gesh;

/// <summary>
/// Shows the whole part a pinch would act on: a vertex as a ball over it, an
/// edge as a thicker bar along it, a face as a tint over the triangle.
/// </summary>
class GPartHighlight
{
	/// <summary>How much bigger than the vertex or edge it covers.</summary>
	const float GROW = 1.6f;
	/// <summary>The face tint sits this far toward the eye, in meters, so it
	/// doesn't z-fight the face.</summary>
	const float FACE_LIFT = 0.001f;

	readonly Material material;
	readonly Material faceMaterial;
	/// <summary>A unit cylinder along z, stretched over an edge.</summary>
	readonly Mesh bar = Mesh.GenerateCylinder(1, 1, Vec3.Forward);
	/// <summary>Refilled with the hovered face each frame it's drawn.</summary>
	readonly Mesh triangle = new();
	readonly Vertex[] corners = new Vertex[3];
	static readonly uint[] TRIANGLE = [0, 1, 2];

	public GPartHighlight()
	{
		material = Material.Unlit.Copy();
		material.Transparency = Transparency.Blend;
		faceMaterial = material.Copy();
		faceMaterial.FaceCull = Cull.None;
		faceMaterial.DepthWrite = false;
	}

	/// <param name="toWorld">Mesh space to world space.</param>
	/// <param name="scale">The world's scale, so sizes match the drawn mesh.</param>
	public void Draw(GEditableMesh mesh, GMeshPart part, Matrix toWorld, float scale, GSettings settings, Color color)
	{
		int[] vs = mesh.PartVertices(part) ?? throw new System.InvalidOperationException($"{part} was removed");
		Vec3 W(int v) => toWorld.Transform(mesh.vertices.values[v].pos);
		switch (part.Type)
		{
			case GMeshPartType.Vertex:
				Mesh.Sphere.Draw(material, Matrix.TS(W(vs[0]), settings.VertexDrawSize * scale * GROW), color);
				break;
			case GMeshPartType.Edge:
			{
				Vec3 a = W(vs[0]), b = W(vs[1]);
				Vec3 along = b - a;
				// Degenerate edges have no direction to draw along.
				if (along.LengthSq == 0) return;
				float thick = settings.EdgeDrawThickness * scale * GROW;
				bar.Draw(material, Matrix.TRS((a + b) / 2, Quat.LookDir(along), new Vec3(thick, thick, along.Length)), color);
				break;
			}
			case GMeshPartType.Face:
			{
				Vec3 head = Input.Head.position;
				for (int i = 0; i < 3; i++)
				{
					Vec3 p = W(vs[i]);
					corners[i] = new Vertex(p + (head - p).Normalized * FACE_LIFT, Vec3.Up);
				}
				triangle.SetData(corners, TRIANGLE);
				triangle.Draw(faceMaterial, Matrix.Identity, color);
				break;
			}
			default:
				throw new System.ArgumentException($"Unknown part {part}");
		}
	}
}
