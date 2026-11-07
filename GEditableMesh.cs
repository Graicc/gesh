using System;
using System.Linq;
using StereoKit;

namespace gesh;

public record struct GEdge(int a, int b);

public record struct GFace(int a, int b, int c);

public enum GMeshPartType
{
	Vertex, Edge, Face
}

public record struct GMeshPart(GMeshPartType Type, int Index);

class GEditableMesh
{
	public Model model = new Model(new Mesh(), Material.PBR);

	public SwapList<Vertex> vertices = new();
	public SwapList<GEdge> edges = new();
	public SwapList<GFace> faces = new();

	public GEditableMesh() { }

	#region editing

	public int AddVertex(Vertex vertex)
	{
		int index = vertices.Add(vertex);
		if (index == vertices.values.Count() - 1) return index;

		for (int edgeIndex = 0; edgeIndex < edges.values.Count(); edgeIndex++)
		{
			if (edges.free.Contains(edgeIndex)) continue;

			var e = edges.values[edgeIndex];

			if (e.a > index)
			{
				edges.values[edgeIndex] = new(edges.values[edgeIndex].a + 1, edges.values[edgeIndex].b);
			}
			if (e.b > index)
			{
				edges.values[edgeIndex] = new(edges.values[edgeIndex].a, edges.values[edgeIndex].b + 1);
			}
		}

		for (int faceIndex = 0; faceIndex < faces.values.Count(); faceIndex++)
		{
			if (faces.free.Contains(faceIndex)) continue;

			var f = faces.values[faceIndex];

			if (f.a > index)
			{
				faces.values[faceIndex] = new(faces.values[faceIndex].a + 1, faces.values[faceIndex].b, faces.values[faceIndex].c);
			}
			if (f.b > index)
			{
				faces.values[faceIndex] = new(faces.values[faceIndex].a, faces.values[faceIndex].b + 1, faces.values[faceIndex].c);
			}
			if (f.c > index)
			{
				faces.values[faceIndex] = new(faces.values[faceIndex].a, faces.values[faceIndex].b, faces.values[faceIndex].c + 1);
			}
		}

		return index;
	}

	public void RemoveVertex(int index)
	{
		vertices.Remove(index);

		for (int edgeIndex = 0; edgeIndex < edges.values.Count(); edgeIndex++)
		{
			if (edges.free.Contains(edgeIndex)) continue;

			var e = edges.values[edgeIndex];

			if (e.a == index || e.b == index)
			{
				edges.Remove(edgeIndex);
				continue;
			}

			if (e.a > index)
			{
				edges.values[edgeIndex] = new(edges.values[edgeIndex].a - 1, edges.values[edgeIndex].b);
			}
			if (e.b > index)
			{
				edges.values[edgeIndex] = new(edges.values[edgeIndex].a, edges.values[edgeIndex].b - 1);
			}
		}

		for (int faceIndex = 0; faceIndex < faces.values.Count(); faceIndex++)
		{
			if (faces.free.Contains(faceIndex)) continue;

			var f = faces.values[faceIndex];

			if (f.a == index || f.b == index || f.c == index)
			{
				faces.Remove(faceIndex);
				continue;
			}

			if (f.a > index)
			{
				faces.values[faceIndex] = new(faces.values[faceIndex].a - 1, faces.values[faceIndex].b, faces.values[faceIndex].c);
			}
			if (f.b > index)
			{
				faces.values[faceIndex] = new(faces.values[faceIndex].a, faces.values[faceIndex].b - 1, faces.values[faceIndex].c);
			}
			if (f.c > index)
			{
				faces.values[faceIndex] = new(faces.values[faceIndex].a, faces.values[faceIndex].b, faces.values[faceIndex].c - 1);
			}
		}
	}

	public int AddEdge(GEdge edge)
	{
		return edges.Add(edge);
	}

	public void RemoveEdge(int index)
	{
		GEdge edge = edges.Remove(index);

		for (int faceIndex = 0; faceIndex < faces.values.Count(); faceIndex++)
		{
			if (faces.free.Contains(faceIndex)) continue;

			var f = faces.values[faceIndex];

			bool e1 = f.a == edge.a || f.b == edge.a || f.c == edge.a;
			bool e2 = f.a == edge.b || f.b == edge.b || f.c == edge.b;
			if (e1 && e2)
			{
				faces.Remove(faceIndex);
			}
		}
	}

	public int AddFace(GFace face)
	{
		return faces.Add(face);
	}

	public void RemoveFace(int index)
	{
		faces.Remove(index);
	}

	#endregion

	#region rendering

	void UpdateMesh()
	{
		model.RootNode.Mesh.SetData(vertices.Items().ToArray(), faces.Items().SelectMany((v) => new[] { (uint)v.a, (uint)v.b, (uint)v.c }).ToArray());
	}

	public void Draw()
	{
		UpdateMesh();
		model.Draw(Matrix.Identity);

		foreach (var v in vertices.Items())
		{
			Mesh.Sphere.Draw(Material.UI, Matrix.TS(v.pos, 0.05f));
		}

		foreach (var e in edges.Items())
		{
			float thickness = 0.05f;
			Color color = Color.Hex(0xFF0000FF);

			Lines.Add([new LinePoint(vertices.values[e.a].pos, color, thickness), new LinePoint(vertices.values[e.b].pos, color, thickness)]);
		}
	}

	#endregion

	public GMeshPart? GetClosestPart(Vec3 point)
	{
		const float minDistance = 0.05f;
		const float minDistanceSquared = minDistance * minDistance;

		GMeshPart? closest = null;
		float closestDistanceSquared = float.MaxValue;

		for (int i = 0; i < vertices.values.Count(); i++)
		{
			if (vertices.free.Contains(i)) continue;

			var v = vertices.values[i];
			var distanceSquared = (v.pos - point).LengthSq;
			if (distanceSquared < minDistanceSquared && distanceSquared < closestDistanceSquared)
			{
				closestDistanceSquared = distanceSquared;
				closest = new(GMeshPartType.Vertex, i);
			}
		}
		if (closestDistanceSquared < float.MaxValue) { return closest; }

		for (int i = 0; i < edges.values.Count(); i++)
		{
			if (edges.free.Contains(i)) continue;

			var e = edges.values[i];
			var distanceSquared = Math.DistanceToLineSq(point, vertices.values[e.a].pos, vertices.values[e.b].pos);
			if (distanceSquared < minDistanceSquared && distanceSquared < closestDistanceSquared)
			{
				closestDistanceSquared = distanceSquared;
				closest = new(GMeshPartType.Edge, i);
			}
		}
		if (closestDistanceSquared < float.MaxValue) { return closest; }

		for (int i = 0; i < faces.values.Count(); i++)
		{
			if (faces.free.Contains(i)) continue;

			var f = faces.values[i];
			var distanceSquared = Math.DistanceToTraingleSq(point, vertices.values[f.a].pos, vertices.values[f.b].pos, vertices.values[f.c].pos);
			if (distanceSquared < minDistanceSquared && distanceSquared < closestDistanceSquared)
			{
				closestDistanceSquared = distanceSquared;
				closest = new(GMeshPartType.Face, i);
			}
		}
		if (closestDistanceSquared < float.MaxValue) { return closest; }

		return closest;
	}
}
