using System;
using System.Collections.Generic;
using System.Linq;
using StereoKit;

namespace gesh;

public record struct GEdge(int a, int b)
{
	public bool Contains(int v) => a == v || b == v;

	public int Other(int v) => a == v ? b : a;

	/// <summary>Same two vertices, in either order.</summary>
	public bool SameVertices(GEdge o) => (a == o.a && b == o.b) || (a == o.b && b == o.a);
}

public record struct GFace(int a, int b, int c)
{
	public bool Contains(int v) => a == v || b == v || c == v;

	public bool Contains(GEdge e) => Contains(e.a) && Contains(e.b);

	public GFace ReplaceVertex(int from, int to) => new(a == from ? to : a, b == from ? to : b, c == from ? to : c);

	public GFace Flipped() => new(a, c, b);

	public GEdge[] Edges() => [new(a, b), new(b, c), new(c, a)];

	/// <summary>Same three vertices, in any order.</summary>
	public bool SameVertices(GFace o) => Contains(o.a) && Contains(o.b) && Contains(o.c);

	/// <summary>True if both faces have the same vertices in the same winding
	/// order, so they face the same direction.</summary>
	public bool SameWinding(GFace o) => this == o || this == new GFace(o.b, o.c, o.a) || this == new GFace(o.c, o.a, o.b);
}

public enum GMeshPartType
{
	Vertex, Edge, Face
}

public record struct GMeshPart(GMeshPartType Type, int Index);

/// <summary>
/// The mesh being edited. The editing functions keep these invariants:
/// - edges and faces only use live vertices, and never the same vertex twice
/// - no two edges share the same vertices, no two faces share the same
///   vertices (whichever way they face)
/// - every side of a face is an edge
/// - no two vertices are on the same spot, so no edge has zero length
/// AddEdge and AddFace throw if a change would break them.
/// It's plain data with no GPU resources, so it works without StereoKit
/// running. GMeshRenderer draws it.
/// </summary>
class GEditableMesh
{
	public SwapList<Vertex> vertices = new();
	public SwapList<GEdge> edges = new();
	public SwapList<GFace> faces = new();
	/// <summary>Each face's paint cell, by face slot. See GPaintAtlas.</summary>
	List<GCell> faceCells = [];
	public GPaintAtlas paint = new();
	/// <summary>Selected vertices, see plans/multiselect.md. An edge or face
	/// is selected when all its vertices are. Editor state: not saved, not
	/// part of the content, and not undone.</summary>
	public readonly HashSet<int> selected = [];

	#region editing

	public int AddVertex(Vertex vertex)
	{
		RequireFreeSpot(vertex.pos, null);
		return vertices.Add(vertex);
	}

	public void MoveVertex(int index, Vec3 pos)
	{
		RequireAlive(new GMeshPart(GMeshPartType.Vertex, index));
		RequireFreeSpot(pos, index);
		vertices.values[index] = vertices.values[index] with { pos = pos };
	}

	/// <summary>Two vertices on the same spot should have been merged, and
	/// would make zero length edges.</summary>
	void RequireFreeSpot(Vec3 pos, int? ignore)
	{
		foreach (int i in vertices.Indices())
		{
			// Vec3.Equals boxes (Vec3 isn't IEquatable), Vector3's == doesn't.
			if (i != ignore && vertices.values[i].pos.v == pos.v) throw new InvalidOperationException($"Vertex placed on top of vertex {i} at {pos}");
		}
	}

	/// <summary>Removes the vertex, and every edge and face using it.</summary>
	public Vertex RemoveVertex(int index)
	{
		foreach (int f in faces.Indices().Where(f => faces.values[f].Contains(index)).ToList()) RemoveFace(f);
		foreach (int e in edges.Indices().Where(e => edges.values[e].Contains(index)).ToList()) edges.Remove(e);
		selected.Remove(index);
		return vertices.Remove(index);
	}

	/// <summary>Removes the selected vertices, and every edge and face using
	/// them.</summary>
	public void RemoveSelected()
	{
		foreach (int v in selected.ToList()) RemoveVertex(v);
	}

	/// <summary>Adds the edge. Breaking an invariant throws instead of being
	/// patched over, so the editing logic that tried it gets fixed.</summary>
	public int AddEdge(GEdge edge)
	{
		if (edge.a == edge.b) throw new InvalidOperationException($"Edge uses the same vertex twice: {edge}");
		if (!vertices.IsAlive(edge.a) || !vertices.IsAlive(edge.b)) throw new InvalidOperationException($"Edge uses a removed vertex: {edge}");
		if (GetEdgeBetween(edge.a, edge.b).HasValue) throw new InvalidOperationException($"Duplicate edge: {edge}");

		return edges.Add(edge);
	}

	/// <summary>Removes the edge, and the faces it's a side of.</summary>
	public GEdge RemoveEdge(int index)
	{
		GEdge edge = edges.values[index];
		foreach (int f in faces.Indices().ToList())
		{
			if (faces.values[f].Contains(edge)) RemoveFace(f);
		}
		return edges.Remove(index);
	}

	/// <summary>Adds the face. Its sides must already be edges. Breaking an
	/// invariant throws, like AddEdge.</summary>
	/// <param name="cell">Paint cell to use, a new blank one if null.</param>
	public int AddFace(GFace face, GCell? cell = null)
	{
		if (face.a == face.b || face.b == face.c || face.c == face.a) throw new InvalidOperationException($"Face uses the same vertex twice: {face}");
		if (!vertices.IsAlive(face.a) || !vertices.IsAlive(face.b) || !vertices.IsAlive(face.c)) throw new InvalidOperationException($"Face uses a removed vertex: {face}");
		if (GetFaceBetween(face).HasValue) throw new InvalidOperationException($"Duplicate face: {face}");
		foreach (GEdge side in face.Edges())
		{
			if (!GetEdgeBetween(side.a, side.b).HasValue) throw new InvalidOperationException($"Face side isn't an edge: {face} {side}");
		}

		int index = faces.Add(face);
		while (faceCells.Count < faces.values.Count) faceCells.Add(default);
		var (a, b, c) = Corners(face);
		faceCells[index] = cell ?? paint.Allocate(paint.SizeFor(a, b, c));
		return index;
	}

	/// <summary>Removes the face, and frees its paint unless it was taken
	/// over by another face (TakeCell).</summary>
	public GFace RemoveFace(int index)
	{
		GFace face = faces.Remove(index);
		// A default cell (size 0) means TakeCell moved it to another face.
		if (faceCells[index].size > 0) paint.Free(faceCells[index]);
		faceCells[index] = default;
		return face;
	}

	public GCell CellOf(int face)
	{
		RequireAlive(new GMeshPart(GMeshPartType.Face, face));
		return faceCells[face];
	}

	/// <summary>Moves a face's paint cell away from it, for another face to use.
	/// The face must be removed afterwards.</summary>
	GCell TakeCell(int face)
	{
		GCell cell = CellOf(face);
		if (cell.size == 0) throw new InvalidOperationException($"Face {face}'s cell was already taken");
		faceCells[face] = default;
		return cell;
	}

	/// <returns>The new vertex, and the new face if exactly one face was made.</returns>
	public (GMeshPart vertex, int? face) Extend(GMeshPart part, Vec3 position)
	{
		RequireAlive(part);
		int newV;
		int? newFace = null;
		switch (part.Type)
		{
			case GMeshPartType.Vertex:
				{
					newV = AddVertex(vertices.values[part.Index] with { pos = position });
					AddEdge(new GEdge(part.Index, newV));
					break;
				}
			case GMeshPartType.Edge:
				{
					GEdge e = edges.values[part.Index];
					newV = AddVertex(vertices.values[e.a] with { pos = position });
					AddEdge(new GEdge(e.a, newV));
					AddEdge(new GEdge(e.b, newV));
					newFace = AddFace(new GFace(newV, e.a, e.b));
					break;
				}
			case GMeshPartType.Face:
				{
					GFace f = faces.values[part.Index];
					newV = AddVertex(vertices.values[f.a] with { pos = position });
					AddEdge(new GEdge(f.a, newV));
					AddEdge(new GEdge(f.b, newV));
					AddEdge(new GEdge(f.c, newV));

					// The three new faces keep the old face's winding, and its
					// paint: they cover the same triangle while the new vertex is
					// still on (or near) it.
					GPaintAtlas.Copy oldPaint = paint.CopyCell(CellOf(part.Index));
					Vec3 oldA = vertices.values[f.a].pos, oldB = vertices.values[f.b].pos, oldC = vertices.values[f.c].pos;
					RemoveFace(part.Index);
					foreach (GFace newFace3 in new GFace[] { new(newV, f.a, f.b), new(newV, f.b, f.c), new(newV, f.c, f.a) })
					{
						var (a, b, c) = Corners(newFace3);
						// A new vertex exactly on a corner's line makes a zero-area face,
						// which has nothing to resample, so it starts blank.
						bool hasArea = Vec3.Cross(b - a, c - a).LengthSq > 0;
						int added = AddFace(newFace3, paint.Allocate(paint.SizeFor(a, b, c), blank: !hasArea));
						if (hasArea) paint.Resample(faceCells[added], a, b, c, oldPaint, oldA, oldB, oldC);
					}
					break;
				}
			default: throw new ArgumentException($"Unknown part {part}");
		}

		return (new GMeshPart(GMeshPartType.Vertex, newV), newFace);
	}

	/// <summary>Flips the face if needed, so its front side faces the viewer.</summary>
	public void OrientFaceToward(int faceIndex, Vec3 viewer)
	{
		RequireAlive(new GMeshPart(GMeshPartType.Face, faceIndex));

		GFace f = faces.values[faceIndex];
		Vec3 a = vertices.values[f.a].pos;
		Vec3 b = vertices.values[f.b].pos;
		Vec3 c = vertices.values[f.c].pos;
		Vec3 normal = Vec3.Cross(b - a, c - a);
		if (Vec3.Dot(normal, viewer - (a + b + c) / 3) < 0)
		{
			faces.values[faceIndex] = f.Flipped();
			// Flipping swaps corners b and c, which swaps the cell's two
			// texture directions. Swapping the pixels back keeps paint in place.
			paint.Transpose(faceCells[faceIndex]);
		}
	}

	/// <summary>Merges vertex `from` into `to`, moving its edges and faces over.</summary>
	public void MergeVertex(int from, int to)
	{
		RequireAlive(new GMeshPart(GMeshPartType.Vertex, from));
		RequireAlive(new GMeshPart(GMeshPartType.Vertex, to));
		if (from == to) throw new InvalidOperationException($"Merging vertex {from} into itself");

		foreach (GEdge e in edges.Items().Where(e => e.Contains(from)).ToList())
		{
			int other = e.Other(from);
			// The edge between the two collapses, RemoveVertex cleans it up.
			// Edges landing on an existing one merge into it.
			if (other != to && !GetEdgeBetween(to, other).HasValue) AddEdge(new GEdge(to, other));
		}

		// Faces on both vertices collapse into an edge, RemoveVertex cleans them up.
		List<int> fromFaces = faces.Indices().Where(i => faces.values[i].Contains(from) && !faces.values[i].Contains(to)).ToList();
		// Sides of faces that cancel out, removed at the end if no face is left
		// on them, so cancelling doesn't leave dangling edges inside the mesh.
		List<GEdge> cancelledSides = [];
		foreach (int fi in fromFaces)
		{
			// Keeps the winding and corner order, so the face keeps facing the
			// same way, and keeps its paint.
			GFace merged = faces.values[fi].ReplaceVertex(from, to);
			int? existing = GetFaceBetween(merged);
			if (!existing.HasValue)
			{
				AddFace(merged, TakeCell(fi));
			}
			else if (!faces.values[existing.Value].SameWinding(merged))
			{
				if (fromFaces.Count == 1)
				{
					// A lone face replaces the one it lands on, flipping it, and
					// brings its paint along.
					RemoveFace(existing.Value);
					AddFace(merged, TakeCell(fi));
				}
				else
				{
					// Two faces on the same vertices pointing opposite ways
					// cancel out.
					RemoveFace(existing.Value);
					cancelledSides.AddRange(merged.Edges());
				}
			}
		}

		RemoveVertex(from);

		foreach (GEdge side in cancelledSides)
		{
			int? edge = GetEdgeBetween(side.a, side.b);
			if (edge.HasValue && !faces.Items().Any(f => f.Contains(side))) RemoveEdge(edge.Value);
		}
	}

	#endregion

	#region painting

	(Vec3 a, Vec3 b, Vec3 c) Corners(GFace f) => (vertices.values[f.a].pos, vertices.values[f.b].pos, vertices.values[f.c].pos);

	/// <summary>Gives faces a cell sized for their current size, keeping their
	/// paint, if they've grown or shrunk past their cell's size. Called when
	/// a drag ends rather than every frame, resizing paint costs a copy.</summary>
	public void RefitPaint(IEnumerable<int> faceIndices)
	{
		foreach (int f in faceIndices)
		{
			GCell old = CellOf(f);
			var (a, b, c) = Corners(faces.values[f]);
			int size = paint.SizeFor(a, b, c);
			if (size == old.size) continue;

			GPaintAtlas.Copy copy = paint.CopyCell(old);
			paint.Free(old);
			GCell cell = paint.Allocate(size, blank: false);
			paint.Rescale(cell, copy);
			faceCells[f] = cell;
		}
	}

	/// <summary>Refits the faces using any of these vertices, see RefitPaint.</summary>
	public void RefitPaintAround(IEnumerable<int> vertexIndices)
	{
		HashSet<int> moved = [.. vertexIndices.Where(vertices.IsAlive)];
		RefitPaint(faces.Indices().Where(f => moved.Contains(faces.values[f].a) || moved.Contains(faces.values[f].b) || moved.Contains(faces.values[f].c)).ToList());
	}

	/// <summary>Changes the paint resolution, resizing every face's paint.</summary>
	public void SetTexelSize(float texelSize)
	{
		if (texelSize <= 0) throw new ArgumentException($"Texel size must be positive, got {texelSize}");
		paint.texelSize = texelSize;
		RefitPaint(faces.Indices().ToList());
	}

	/// <summary>The paint color on the closest face within radius, for picking
	/// a color off the mesh.</summary>
	public Color32? ColorAt(Vec3 point, float radius)
	{
		GMeshPart? face = GetClosestFace(point, radius);
		if (!face.HasValue) return null;
		var (a, b, c) = Corners(faces.values[face.Value.Index]);
		if (Vec3.Cross(b - a, c - a).LengthSq == 0) return null;
		var (s, t) = GPaintAtlas.PlaneCoordinates(point, a, b, c);
		return paint.ColorAt(CellOf(face.Value.Index), s, t);
	}

	/// <summary>Paints every face within `radius` of the segment from-to,
	/// a brush stroke between two frames.</summary>
	/// <returns>Whether the brush touched any face.</returns>
	public bool PaintStroke(Vec3 from, Vec3 to, float radius, Color32 color)
	{
		bool touched = false;
		// Faces near any point along the segment, spaced well under the radius.
		int steps = int.Clamp((int)MathF.Ceiling((to - from).Length / (radius / 2)), 1, 64);
		float radiusSq = radius * radius;
		// Box around the stroke, to skip far faces without the exact check.
		Vec3 strokeMin = Vec3.Min(from, to) - new Vec3(radius), strokeMax = Vec3.Max(from, to) + new Vec3(radius);
		foreach (int fi in faces.Indices())
		{
			var (a, b, c) = Corners(faces.values[fi]);
			Vec3 faceMin = Vec3.Min(a, Vec3.Min(b, c)), faceMax = Vec3.Max(a, Vec3.Max(b, c));
			if (faceMin.x > strokeMax.x || faceMin.y > strokeMax.y || faceMin.z > strokeMax.z ||
				faceMax.x < strokeMin.x || faceMax.y < strokeMin.y || faceMax.z < strokeMin.z) continue;

			bool near = false;
			for (int i = 0; i <= steps && !near; i++)
			{
				near = Math.DistanceToTraingleSq(Vec3.Lerp(from, to, i / (float)steps), a, b, c) < radiusSq;
			}
			if (near) paint.PaintCapsule(faceCells[fi], a, b, c, from, to, radius, color);
			touched |= near;
		}
		return touched;
	}

	public void FillFace(int face, Color32 color)
	{
		paint.Fill(CellOf(face), color);
	}

	#endregion

	#region querying

	/// <summary>Changes whenever the mesh's content does (positions, edges,
	/// faces and their winding, paint).</summary>
	public int ContentHash()
	{
		HashCode hash = new();
		foreach (int i in vertices.Indices())
		{
			hash.Add(i);
			hash.Add(vertices.values[i].pos);
		}
		foreach (GEdge e in edges.Items()) hash.Add(e);
		foreach (GFace f in faces.Items()) hash.Add(f);
		hash.Add(paint.Version);
		return hash.ToHashCode();
	}

	public int? GetEdgeBetween(int a, int b)
	{
		GEdge edge = new(a, b);
		foreach (int i in edges.Indices())
		{
			if (edges.values[i].SameVertices(edge)) return i;
		}
		return null;
	}

	/// <summary>A face on the same three vertices, either way around.</summary>
	public int? GetFaceBetween(GFace face)
	{
		foreach (int i in faces.Indices())
		{
			if (faces.values[i].SameVertices(face)) return i;
		}
		return null;
	}

	public bool IsAlive(GMeshPart part) => part.Type switch
	{
		GMeshPartType.Vertex => vertices.IsAlive(part.Index),
		GMeshPartType.Edge => edges.IsAlive(part.Index),
		GMeshPartType.Face => faces.IsAlive(part.Index),
		_ => throw new ArgumentException($"Unknown part {part}"),
	};

	void RequireAlive(GMeshPart part)
	{
		if (!IsAlive(part)) throw new InvalidOperationException($"{part} was removed");
	}

	/// <summary>The vertices a part is made of, or null if it was removed.</summary>
	public int[]? PartVertices(GMeshPart part)
	{
		if (!IsAlive(part)) return null;
		return part.Type switch
		{
			GMeshPartType.Vertex => [part.Index],
			GMeshPartType.Edge => [edges.values[part.Index].a, edges.values[part.Index].b],
			GMeshPartType.Face => [faces.values[part.Index].a, faces.values[part.Index].b, faces.values[part.Index].c],
			_ => throw new ArgumentException($"Unknown part {part}"),
		};
	}


	/// <summary>Everything about the mesh but its paint pixels, for undo. The
	/// selection isn't included: it's not part of the content.</summary>
	public sealed record Snapshot(SwapList<Vertex> vertices, SwapList<GEdge> edges, SwapList<GFace> faces, List<GCell> faceCells, GPaintAtlas.AllocatorState paint);

	public Snapshot TakeSnapshot() => new(vertices.Clone(), edges.Clone(), faces.Clone(), [.. faceCells], paint.SaveAllocator());

	/// <summary>Back to a snapshot. Selected vertices that don't exist there
	/// are dropped.</summary>
	public void Restore(Snapshot snapshot)
	{
		vertices = snapshot.vertices.Clone();
		edges = snapshot.edges.Clone();
		faces = snapshot.faces.Clone();
		faceCells = [.. snapshot.faceCells];
		paint.RestoreAllocator(snapshot.paint);
		selected.RemoveWhere(v => v >= vertices.values.Count || !vertices.IsAlive(v));
	}

	/// <summary>Is the part selected, that is, all its vertices?</summary>
	public bool IsSelected(GMeshPart part) => PartVertices(part)?.All(selected.Contains) ?? throw new InvalidOperationException($"{part} was removed");

	/// <summary>Removes a part: a vertex with everything using it, an edge
	/// with the faces it's a side of, or just a face (its edges stay).</summary>
	public void RemovePart(GMeshPart part)
	{
		RequireAlive(part);
		switch (part.Type)
		{
			case GMeshPartType.Vertex: RemoveVertex(part.Index); break;
			case GMeshPartType.Edge: RemoveEdge(part.Index); break;
			case GMeshPartType.Face: RemoveFace(part.Index); break;
			default: throw new ArgumentException($"Unknown part {part}");
		}
	}

	/// <summary>The middle of a live part: the vertex itself, an edge's
	/// midpoint, a face's centroid.</summary>
	public Vec3 PartPoint(GMeshPart part)
	{
		RequireAlive(part);
		int[] vs = PartVertices(part)!;
		Vec3 sum = Vec3.Zero;
		foreach (int v in vs) sum += vertices.values[v].pos;
		return sum / vs.Length;
	}

	/// <summary>The closest face within radius.</summary>
	public GMeshPart? GetClosestFace(Vec3 point, float radius)
	{
		GMeshPart? closest = null;
		float closestSq = radius * radius;
		foreach (int i in faces.Indices())
		{
			var (a, b, c) = Corners(faces.values[i]);
			float d = Math.DistanceToTraingleSq(point, a, b, c);
			if (d < closestSq)
			{
				closestSq = d;
				closest = new(GMeshPartType.Face, i);
			}
		}
		return closest;
	}

	/// <summary>The closest vertex within radius, other than the excluded ones.</summary>
	public int? GetClosestVertex(Vec3 point, float radius, int[] exclude)
	{
		int? closest = null;
		float closestSq = radius * radius;
		foreach (int i in vertices.Indices())
		{
			if (exclude.Contains(i)) continue;
			float d = (vertices.values[i].pos - point).LengthSq;
			if (d < closestSq)
			{
				closestSq = d;
				closest = i;
			}
		}
		return closest;
	}

	/// <summary>Finds the closest part within radius, preferring vertices,
	/// then edges, then faces.</summary>
	/// <summary>The closest part: a vertex within radius, else an edge or face
	/// within half of it. Edges and faces are big targets, so half keeps them
	/// from catching pinches meant for the space around them.</summary>
	public GMeshPart? GetClosestPart(Vec3 point, float radius)
	{
		GMeshPart? Closest(GMeshPartType type, IEnumerable<int> indices, Func<int, float> distanceSq)
		{
			GMeshPart? closest = null;
			float r = type == GMeshPartType.Vertex ? radius : radius / 2;
			float closestSq = r * r;
			foreach (int i in indices)
			{
				float d = distanceSq(i);
				if (d < closestSq)
				{
					closestSq = d;
					closest = new(type, i);
				}
			}
			return closest;
		}

		Vec3 Pos(int v) => vertices.values[v].pos;

		return Closest(GMeshPartType.Vertex, vertices.Indices(), i => (Pos(i) - point).LengthSq)
			?? Closest(GMeshPartType.Edge, edges.Indices(), i => Math.DistanceToLineSq(point, Pos(edges.values[i].a), Pos(edges.values[i].b)))
			?? Closest(GMeshPartType.Face, faces.Indices(), i => Math.DistanceToTraingleSq(point, Pos(faces.values[i].a), Pos(faces.values[i].b), Pos(faces.values[i].c)));
	}

	#endregion
}
