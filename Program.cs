using System;
using System.Collections.Generic;
using System.Linq;
using StereoKit;

namespace gesh;

using GEdge = (int, int);
using GFace = (int, int, int);

class SwapList<T>
{
	public List<T> values = new();
	public Queue<int> free = new();

	public int Add(T value)
	{
		if (free.Count == 0)
		{
			values.Add(value);
			return values.Count() - 1;
		}

		int idx = free.Dequeue();
		values[idx] = value;
		return idx;
	}

	public T Remove(int index)
	{
		free.Append(index);
		return values[index];
	}

	public IEnumerable<T> Items()
	{
		int i = 0;
		return values.Where((_) => !free.Contains(i++));
	}

	public void Map()
	{

	}
}

class GEditableMesh
{
	public Model model = new Model(new Mesh(), Material.UI);

	SwapList<Vertex> vertices = new();
	SwapList<GEdge> edges = new();
	SwapList<GFace> faces = new();

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

			if (e.Item1 > index)
			{
				edges.values[edgeIndex] = (edges.values[edgeIndex].Item1 + 1, edges.values[edgeIndex].Item2);
			}
			if (e.Item2 > index)
			{
				edges.values[edgeIndex] = (edges.values[edgeIndex].Item1, edges.values[edgeIndex].Item2 + 1);
			}
		}

		for (int faceIndex = 0; faceIndex < faces.values.Count(); faceIndex++)
		{
			if (faces.free.Contains(faceIndex)) continue;

			var f = faces.values[faceIndex];

			if (f.Item1 > index)
			{
				faces.values[faceIndex] = (faces.values[faceIndex].Item1 + 1, faces.values[faceIndex].Item2, faces.values[faceIndex].Item3);
			}
			if (f.Item2 > index)
			{
				faces.values[faceIndex] = (faces.values[faceIndex].Item1, faces.values[faceIndex].Item2 + 1, faces.values[faceIndex].Item3);
			}
			if (f.Item3 > index)
			{
				faces.values[faceIndex] = (faces.values[faceIndex].Item1, faces.values[faceIndex].Item2, faces.values[faceIndex].Item3 + 1);
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

			if (e.Item1 == index || e.Item2 == index)
			{
				edges.Remove(edgeIndex);
				continue;
			}

			if (e.Item1 > index)
			{
				edges.values[edgeIndex] = (edges.values[edgeIndex].Item1 - 1, edges.values[edgeIndex].Item2);
			}
			if (e.Item2 > index)
			{
				edges.values[edgeIndex] = (edges.values[edgeIndex].Item1, edges.values[edgeIndex].Item2 - 1);
			}
		}

		for (int faceIndex = 0; faceIndex < faces.values.Count(); faceIndex++)
		{
			if (faces.free.Contains(faceIndex)) continue;

			var f = faces.values[faceIndex];

			if (f.Item1 == index || f.Item2 == index || f.Item3 == index)
			{
				faces.Remove(faceIndex);
				continue;
			}

			if (f.Item1 > index)
			{
				faces.values[faceIndex] = (faces.values[faceIndex].Item1 - 1, faces.values[faceIndex].Item2, faces.values[faceIndex].Item3);
			}
			if (f.Item2 > index)
			{
				faces.values[faceIndex] = (faces.values[faceIndex].Item1, faces.values[faceIndex].Item2 - 1, faces.values[faceIndex].Item3);
			}
			if (f.Item3 > index)
			{
				faces.values[faceIndex] = (faces.values[faceIndex].Item1, faces.values[faceIndex].Item2, faces.values[faceIndex].Item3 - 1);
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

			bool e1 = f.Item1 == edge.Item1 || f.Item2 == edge.Item1 || f.Item3 == edge.Item1;
			bool e2 = f.Item1 == edge.Item2 || f.Item2 == edge.Item2 || f.Item3 == edge.Item2;
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
		model.RootNode.Mesh.SetData(vertices.Items().ToArray(), faces.Items().SelectMany((v) => new[] { (uint)v.Item1, (uint)v.Item2, (uint)v.Item3 }).ToArray());
	}

	public void Draw()
	{
		UpdateMesh();
		model.Draw(Matrix.Identity);
		Console.WriteLine($"{model.RootNode.Mesh.VertCount}");

		foreach (var v in vertices.Items())
		{
			Mesh.Sphere.Draw(Material.UI, Matrix.TS(v.pos, 0.1f));
		}

		foreach (var e in edges.Items())
		{
			float thickness = 0.05f;
			Color color = Color.Hex(0xFF0000FF);

			Lines.Add([new LinePoint(vertices.values[e.Item1].pos, color, thickness), new LinePoint(vertices.values[e.Item2].pos, color, thickness)]);
		}
	}

	#endregion
}

class Program
{
	public static void Main()
	{
		// Initialize StereoKit
		SKSettings settings = new SKSettings
		{
			appName = "gesh",
		};
		if (!SK.Initialize(settings))
			return;


		// Create assets used by the app
		Pose cubePose = new Pose(0, 0, -0.5f);
		Model cube = Model.FromMesh(
			Mesh.GenerateRoundedCube(Vec3.One * 0.1f, 0.02f),
			Material.UI);

		Matrix floorTransform = Matrix.TS(0, -1.5f, 0, new Vec3(30, 0.1f, 30));
		Material floorMaterial = new Material("floor.hlsl");
		floorMaterial.Transparency = Transparency.Blend;

		// cube.RootNode.Mesh.SetData()

		GEditableMesh editableMesh = new GEditableMesh();
		editableMesh.AddVertex(new Vertex(new Vec3(0, -0.5f, -0.5f), Vec3.Up));
		editableMesh.AddVertex(new Vertex(new Vec3(0, 0.5f, -0.5f), Vec3.Up));
		editableMesh.AddVertex(new Vertex(new Vec3(1.0f, 0f, -0.5f), Vec3.Up));
		editableMesh.AddEdge(new GEdge(0, 1));
		editableMesh.AddEdge(new GEdge(1, 2));
		editableMesh.AddEdge(new GEdge(2, 0));
		// editableMesh.AddFace(new GFace(0, 1, 2));
		editableMesh.AddFace(new GFace(2, 1, 0));

		// Core application loop
		SK.Run(() =>
		{
			if (Device.DisplayBlend == DisplayBlend.Opaque)
				Mesh.Cube.Draw(floorMaterial, floorTransform);

			// UI.Handle("Cube", ref cubePose, cube.Bounds);
			// cube.Draw(cubePose.ToMatrix());
			editableMesh.Draw();
		});
	}
}