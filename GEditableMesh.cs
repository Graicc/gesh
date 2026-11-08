using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
	public Model model = new Model(new Mesh(), new Material("mesh.hlsl"));

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

	public Vertex RemoveVertex(int index)
	{
		Vertex vertex = vertices.Remove(index);

		for (int edgeIndex = 0; edgeIndex < edges.values.Count(); edgeIndex++)
		{
			if (edges.free.Contains(edgeIndex)) continue;

			var e = edges.values[edgeIndex];

			if (e.a == index || e.b == index)
			{
				edges.Remove(edgeIndex);
				continue;
			}

			// if (e.a > index)
			// {
			// 	edges.values[edgeIndex] = new(edges.values[edgeIndex].a - 1, edges.values[edgeIndex].b);
			// }
			// if (e.b > index)
			// {
			// 	edges.values[edgeIndex] = new(edges.values[edgeIndex].a, edges.values[edgeIndex].b - 1);
			// }
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

			// if (f.a > index)
			// {
			// 	faces.values[faceIndex] = new(faces.values[faceIndex].a - 1, faces.values[faceIndex].b, faces.values[faceIndex].c);
			// }
			// if (f.b > index)
			// {
			// 	faces.values[faceIndex] = new(faces.values[faceIndex].a, faces.values[faceIndex].b - 1, faces.values[faceIndex].c);
			// }
			// if (f.c > index)
			// {
			// 	faces.values[faceIndex] = new(faces.values[faceIndex].a, faces.values[faceIndex].b, faces.values[faceIndex].c - 1);
			// }
		}

		return vertex;
	}

	public int AddEdge(GEdge edge)
	{
		return edges.Add(edge);
	}

	public GEdge RemoveEdge(int index)
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

		return edge;
	}

	public int AddFace(GFace face)
	{
		return faces.Add(face);
	}

	public GFace RemoveFace(int index)
	{
		return faces.Remove(index);
	}

	public GMeshPart Extend(GMeshPart part, Vec3 position)
	{
		int newV = -1;
		switch (part.Type)
		{
			case GMeshPartType.Vertex:
				{
					Vertex v = vertices.values[part.Index];
					Vertex newVertex = new Vertex(position, v.norm, v.uv, v.col);
					newV = AddVertex(newVertex);
					AddEdge(new GEdge(part.Index, newV));
					break;
				}
			case GMeshPartType.Edge:
				{
					GEdge e = edges.values[part.Index];
					Vertex va = vertices.values[e.a];
					Vertex newVertex = new Vertex(position, va.norm, va.uv, va.col);
					newV = AddVertex(newVertex);

					AddEdge(new GEdge(e.a, newV));
					AddEdge(new GEdge(e.b, newV));

					AddFace(new GFace(newV, e.a, e.b));
					break;
				}
			case GMeshPartType.Face:
				{
					GFace f = faces.values[part.Index];

					Vertex va = vertices.values[f.a];
					Vertex newVertex = new Vertex(position, va.norm, va.uv, va.col);
					newV = AddVertex(newVertex);

					AddEdge(new GEdge(f.a, newV));
					AddEdge(new GEdge(f.b, newV));
					AddEdge(new GEdge(f.c, newV));

					RemoveFace(part.Index);

					AddFace(new GFace(newV, f.a, f.b));
					AddFace(new GFace(newV, f.b, f.c));
					AddFace(new GFace(newV, f.c, f.a));

					break;
				}
		}

		return new GMeshPart(GMeshPartType.Vertex, newV);
	}

	public void TryMerge(GMeshPart toRemove, GMeshPart target)
	{
		if (toRemove.Type == GMeshPartType.Vertex && target.Type == GMeshPartType.Vertex)
		{
			List<GMeshPart> removeNeighbors = GetNeighbors(toRemove);
			foreach (GMeshPart part in removeNeighbors)
			{
				switch (part.Type)
				{
					case GMeshPartType.Edge:
						{
							// GEdge e = RemoveEdge(part.Index);
							GEdge e = edges.values[part.Index];
							int otherEnd = e.a == toRemove.Index ? e.b : e.a;
							if (otherEnd == target.Index) continue;

							int? edgeBetween = GetEdgeBetween(target.Index, otherEnd);
							if (!edgeBetween.HasValue)
							{
								AddEdge(new GEdge(target.Index, otherEnd));
							}
							break;
						}
					case GMeshPartType.Face:
						{
							// GFace f = RemoveFace(part.Index);
							GFace f = faces.values[part.Index];

							List<int> otherEnds = [f.a, f.b, f.c];
							otherEnds.Remove(toRemove.Index);
							if (otherEnds.Contains(target.Index)) { continue; }

							int? faceBetween = GetFaceBetween(target.Index, otherEnds[0], otherEnds[1]);
							if (!faceBetween.HasValue)
							{
								AddFace(new GFace(target.Index, otherEnds[0], otherEnds[1]));
							}

							break;
						}
				}
			}
			RemoveVertex(toRemove.Index);
		}
	}

	#endregion

	#region rendering

	void UpdateMesh()
	{
		model.RootNode.Mesh.SetData(vertices.values.ToArray(), faces.Items().SelectMany((v) => new[] { (uint)v.a, (uint)v.b, (uint)v.c }).ToArray());
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

	public void DrawUI()
	{
		UI.Text($"{string.Join(", ", vertices.Items())}");
		UI.NextLine();
		UI.Text($"{string.Join(", ", edges.Items().ToArray())}");
		UI.NextLine();
		UI.Text($"{string.Join(", ", faces.Items().ToArray())}");
	}

	#endregion

	#region querying

	public int? GetEdgeBetween(int a, int b)
	{
		for (int i = 0; i < edges.values.Count(); i++)
		{
			if (edges.free.Contains(i)) continue;
			var e = edges.values[i];
			if ((e.a == a || e.b == a) && (e.a == b || e.b == b))
			{
				return i;
			}
		}

		return null;
	}

	public int? GetFaceBetween(int a, int b, int c)
	{
		for (int i = 0; i < faces.values.Count(); i++)
		{
			if (faces.free.Contains(i)) continue;
			var f = faces.values[i];
			if (
				(f.a == a || f.b == a || f.c == a) &&
				(f.a == b || f.b == b || f.c == b) &&
				(f.a == c || f.b == c || f.c == c)
			)
			{
				return i;
			}
		}

		return null;
	}

	public List<GMeshPart> GetNeighbors(GMeshPart part)
	{
		List<GMeshPart> parts = [];

		switch (part.Type)
		{
			case GMeshPartType.Vertex:
				{
					// Edges incident to vertex
					for (int i = 0; i < edges.values.Count(); i++)
					{
						if (edges.free.Contains(i)) continue;
						var e = edges.values[i];
						if (e.a == part.Index || e.b == part.Index)
						{
							parts.Add(new GMeshPart(GMeshPartType.Edge, i));
						}
					}
					// Faces containing vertex
					for (int i = 0; i < faces.values.Count(); i++)
					{
						if (faces.free.Contains(i)) continue;
						var f = faces.values[i];
						if (f.a == part.Index || f.b == part.Index || f.c == part.Index)
						{
							parts.Add(new GMeshPart(GMeshPartType.Face, i));
						}
					}

					break;
				}
			case GMeshPartType.Edge:
				{
					var edge = edges.values[part.Index];
					// Vertices incident to edge
					parts.Add(new GMeshPart(GMeshPartType.Vertex, edge.a));
					parts.Add(new GMeshPart(GMeshPartType.Vertex, edge.b));
					// Face containing to edge
					for (int i = 0; i < faces.values.Count(); i++)
					{
						if (faces.free.Contains(i)) continue;
						var f = faces.values[i];
						if ((f.a == edge.a || f.b == edge.a || f.c == edge.a) && (f.a == edge.b || f.b == edge.b || f.c == edge.b))
						{
							parts.Add(new GMeshPart(GMeshPartType.Face, i));
						}
					}

					break;
				}
			case GMeshPartType.Face:
				{
					var face = faces.values[part.Index];
					// Vertices incident to face
					parts.Add(new GMeshPart(GMeshPartType.Vertex, face.a));
					parts.Add(new GMeshPart(GMeshPartType.Vertex, face.b));
					parts.Add(new GMeshPart(GMeshPartType.Vertex, face.c));
					// Edges incident to face
					for (int i = 0; i < edges.values.Count(); i++)
					{
						if (edges.free.Contains(i)) continue;
						var e = edges.values[i];
						if ((face.a == e.a || face.b == e.a || face.c == e.a) && (face.a == e.b || face.b == e.b || face.c == e.b))
						{
							parts.Add(new GMeshPart(GMeshPartType.Edge, i));
						}
					}

					break;
				}
		}
		return parts;
	}

	public GMeshPart? GetClosestPart(Vec3 point)
	{
		return GetClosestPart(point, []);
	}

	public GMeshPart? GetClosestPart(Vec3 point, List<GMeshPart> ignore)
	{
		const float minDistance = 0.05f;
		const float minDistanceSquared = minDistance * minDistance;

		GMeshPart? closest = null;
		float closestDistanceSquared = float.MaxValue;

		for (int i = 0; i < vertices.values.Count(); i++)
		{
			if (vertices.free.Contains(i)) continue;
			if (ignore.Contains(new GMeshPart(GMeshPartType.Vertex, i))) continue;

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
			if (ignore.Contains(new GMeshPart(GMeshPartType.Edge, i))) continue;

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
			if (ignore.Contains(new GMeshPart(GMeshPartType.Face, i))) continue;

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

	#endregion

}
