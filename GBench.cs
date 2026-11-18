using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using StereoKit;

namespace gesh;

/// <summary>
/// A scripted scene for measuring performance with nobody wearing the
/// headset, see plans/perf.md. Runs when a file named "bench" is in the saves
/// folder: builds a big painted mesh, keeps it moving, logs frame timings,
/// saves it with a thumbnail, and quits.
/// </summary>
class GBench
{
	const double WARMUP_SECONDS = 10;
	const double MEASURE_SECONDS = 30;
	/// <summary>Face count when the flag file doesn't give one.</summary>
	const int DEFAULT_FACES = 1000;
	public readonly int faces;

	readonly List<double> frameMs = [];
	readonly Stopwatch frame = new();
	/// <summary>Total time per section over the measured frames, see Mark.</summary>
	readonly Dictionary<string, double> sectionMs = new();
	double lastMark;
	bool measuring;
	double startTime = -1;
	bool saved;

	/// <summary>The running bench, for marking sections deep in the code
	/// while profiling. Null when not benching.</summary>
	public static GBench? Current;

	public static string FlagPath => Path.Combine(GFiles.FolderPath(), "bench");

	/// <summary>A bench run is requested by putting a "bench" file next to the saves.</summary>
	/// <summary>Paint texel size to bench with.</summary>
	public readonly float texelSize;

	/// <summary>The flag file can hold the face count and paint texel size
	/// to bench with, like "1000 0.002".</summary>
	public static GBench? FromFlag()
	{
		if (!File.Exists(FlagPath)) return Current = null;
		string[] args = File.ReadAllText(FlagPath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		int faces = args.Length > 0 && int.TryParse(args[0], out int f) ? f : DEFAULT_FACES;
		float texel = args.Length > 1 && float.TryParse(args[1], System.Globalization.CultureInfo.InvariantCulture, out float t) ? t : GPaintAtlas.DEFAULT_TEXEL_SIZE;
		return Current = new GBench(faces, texel);
	}

	GBench(int faces, float texelSize)
	{
		this.faces = faces;
		this.texelSize = texelSize;
	}

	/// <summary>The starting triangle, extended at random until it has about
	/// `faces` faces, pushed out a little each time so it's a lumpy shape, with
	/// paint strokes on it.</summary>
	public GDocument CreateDocument()
	{
		var rng = new Random(1234);
		GDocument doc = new(GDocument.Triangle(), GWorld.Identity, "bench");
		GEditableMesh mesh = doc.mesh;
		mesh.SetTexelSize(texelSize);
		while (mesh.faces.Indices().Count() < faces)
		{
			var faces = mesh.faces.Indices().ToList();
			int f = faces[rng.Next(faces.Count)];
			GFace face = mesh.faces.values[f];
			Vec3 a = mesh.vertices.values[face.a].pos, b = mesh.vertices.values[face.b].pos, c = mesh.vertices.values[face.c].pos;
			Vec3 normal = Vec3.Cross(b - a, c - a).Normalized;
			float size = MathF.Sqrt(Vec3.Cross(b - a, c - a).Length);
			mesh.Extend(new GMeshPart(GMeshPartType.Face, f), (a + b + c) / 3 + normal * size * 0.15f);
		}
		for (int i = 0; i < 30; i++)
		{
			Vec3 from = new((float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f, -0.5f);
			mesh.PaintStroke(from, from + new Vec3(0.2f, 0.1f, 0), 0.05f, new Color32((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 255));
		}
		doc.world = new GWorld(new Vec3(-0.4f, 0, -0.3f), Quat.Identity, 1);
		return doc;
	}

	/// <summary>Extending the starting triangle (about a meter, so the
	/// biggest paint cells) after painting it: the slowest single edit.</summary>
	static double BigExtendMs(float texelSize)
	{
		GEditableMesh mesh = GDocument.Triangle();
		mesh.SetTexelSize(texelSize);
		int face = mesh.faces.Indices().First();
		GFace f = mesh.faces.values[face];
		Vec3 a = mesh.vertices.values[f.a].pos, b = mesh.vertices.values[f.b].pos, c = mesh.vertices.values[f.c].pos;
		mesh.PaintStroke(a, b, 0.05f, new Color32(200, 40, 40, 255));
		var watch = Stopwatch.StartNew();
		mesh.Extend(new GMeshPart(GMeshPartType.Face, face), (a + b + c) / 3);
		return watch.Elapsed.TotalMilliseconds;
	}

	public void BeginFrame()
	{
		frame.Restart();
		lastMark = 0;
	}

	/// <summary>Ends a section of the frame started at the previous mark, so
	/// the log can say where the time goes.</summary>
	public void Mark(string section)
	{
		double now = frame.Elapsed.TotalMilliseconds;
		if (measuring) sectionMs[section] = sectionMs.GetValueOrDefault(section) + now - lastMark;
		lastMark = now;
	}

	/// <summary>Moves things like a user would, and records the frame's time.
	/// Call at the end of the frame.</summary>
	public void EndFrame(GDocument doc, GSettings settings)
	{
		if (startTime < 0) startTime = Time.Total;
		double t = Time.Total - startTime;

		measuring = t > WARMUP_SECONDS;

		// Slow world turn, and one vertex moving like it's held, so the
		// per-frame mesh rebuilds and culling all run.
		doc.world = doc.world with { rotation = Quat.FromAngles(0, (float)t * 10, 0) };
		int v = doc.mesh.vertices.Indices().Last();
		Vec3 pos = doc.mesh.vertices.values[v].pos;
		doc.mesh.MoveVertex(v, pos + new Vec3(MathF.Sin((float)t) * 0.001f, 0, 0));
		Mark("bench move");
		// A brush stroke every frame, like painting.
		Vec3 brush = new(0.3f + MathF.Sin((float)t * 0.7f) * 0.3f, MathF.Cos((float)t * 0.5f) * 0.3f, -0.45f);
		doc.mesh.PaintStroke(brush, brush + new Vec3(0.01f, 0, 0), settings.brushRadius, new Color32(40, 200, 120, 255));
		Mark("bench paint");

		frame.Stop();
		if (measuring) frameMs.Add(frame.Elapsed.TotalMilliseconds);

		if (t > WARMUP_SECONDS + MEASURE_SECONDS && !saved)
		{
			saved = true;
			frameMs.Sort();
			double P(double q) => frameMs[(int)(q * (frameMs.Count - 1))];
			Log.Info($"BENCH cpu_ms p50={P(0.5):0.00} p95={P(0.95):0.00} max={frameMs[^1]:0.00} frames={frameMs.Count} faces={doc.mesh.faces.Indices().Count()}");
			Log.Info($"BENCH paint memory: {doc.mesh.paint.pages.Count} pages, {doc.mesh.paint.pages.Count * GPaintAtlas.PAGE * GPaintAtlas.PAGE * 4 / 1e6:0.0} MB");
			// Twice: the first includes compiling the code on first use.
			Log.Info($"BENCH big face extend ms: first {BigExtendMs(texelSize):0.00}, again {BigExtendMs(texelSize):0.00}, texel {texelSize * 1000:0.0}mm");
			Log.Info("BENCH sections ms/frame: " + string.Join(", ", sectionMs.Select(kv => $"{kv.Key}={kv.Value / frameMs.Count:0.00}")));
			GSaver.SaveWithThumbnail(doc, () => GFiles.PathFor("bench"), error =>
			{
				Log.Info(error ?? "BENCH saved " + GFiles.PathFor("bench"));
				SK.Quit();
			});
		}
	}
}
