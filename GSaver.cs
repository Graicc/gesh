using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using StereoKit;

namespace gesh;

/// <summary>The parts of saving that need StereoKit running: thumbnails,
/// autosave timing, and what to open on startup. GFiles does the file work.</summary>
static class GSaver
{
	/// <summary>The mesh, floor and handles draw on this layer, so thumbnails
	/// can leave out the menu and hands.</summary>
	public const RenderLayer SCENE_LAYER = RenderLayer.Layer1;
	const int THUMBNAIL_SIZE = 256;

	/// <summary>Saves the document with a thumbnail of the user's view. The
	/// thumbnail renders at the end of the frame, so the file lands then.</summary>
	/// <param name="onDone">Gets an error message, or null on success.</param>
	public static void SaveWithThumbnail(GDocument doc, Func<string> path, Action<string?> onDone)
	{
		Matrix camera = Input.Head.ToMatrix();
		Matrix projection = Matrix.Perspective(90, 1, 0.02f, 100);
		Renderer.Screenshot((data, format, width, height) =>
		{
			byte[] rgba = new byte[width * height * 4];
			Marshal.Copy(data, rgba, 0, rgba.Length);
			// The background clears to transparent, put it on light gray so
			// black edges stay visible.
			const int BACKGROUND = 200;
			for (int i = 0; i < rgba.Length; i += 4)
			{
				int alpha = rgba[i + 3];
				for (int c = 0; c < 3; c++) rgba[i + c] = (byte)((rgba[i + c] * alpha + BACKGROUND * (255 - alpha)) / 255);
				rgba[i + 3] = 255;
			}
			onDone(TrySave(path, doc, GPng.Encode(rgba, width, height)));
		}, camera, projection, THUMBNAIL_SIZE, THUMBNAIL_SIZE, SCENE_LAYER);
	}

	/// <summary>Saving can fail for reasons outside the app (disk full, file
	/// locked), so those become a message for the user instead of a crash.</summary>
	public static string? TrySave(Func<string> path, GDocument doc, byte[]? thumbnailPng)
	{
		try
		{
			GFiles.Save(path(), doc, thumbnailPng);
			doc.savedHash = doc.mesh.ContentHash();
			return null;
		}
		catch (IOException e) { return $"Save failed: {e.Message}"; }
		catch (UnauthorizedAccessException e) { return $"Save failed: {e.Message}"; }
	}

	/// <summary>The autosave if it's the newest file (the app quit or crashed
	/// with unsaved work), otherwise a new document.</summary>
	/// <summary>Nothing has ever been saved here, not even an autosave.</summary>
	public static bool IsFirstLaunch()
	{
		try { return GFiles.List().Count == 0; }
		// An unusable saves folder isn't a first launch, it's a problem
		// StartupDocument reports.
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
	}

	/// <summary>Example models from Assets/samples, copied into the saves
	/// folder on first launch so the Files list starts with something to open.
	/// Only then, so one the user deletes stays deleted.</summary>
	static readonly string[] SAMPLES = ["fox", "G"];

	public static void AddSamples()
	{
		foreach (string name in SAMPLES)
		{
			// Shipped with the app, so a missing one is a packaging bug.
			byte[] data = Platform.ReadFileBytes($"samples/{name}.glb") ?? throw new InvalidOperationException($"Assets/samples/{name}.glb is missing");
			try { File.WriteAllBytes(GFiles.PathFor(name), data); }
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Err($"Couldn't add the {name} sample: {e.Message}"); }
		}
	}

	public static GDocument StartupDocument()
	{
		try
		{
			GFileEntry? newest = GFiles.List().FirstOrDefault();
			if (newest?.name != GFiles.AUTOSAVE_NAME) return GDocument.New();

			GDocument doc = GFiles.Load(newest.path);
			// It wasn't saved under its real name, so it counts as unsaved work.
			doc.savedHash = 0;
			return doc;
		}
		catch (Exception e)
		{
			// The saves folder can be unusable, or the autosave broken (files
			// are outside input, not gesh's logic), so start fresh rather than
			// crash on every launch.
			Log.Err($"Couldn't restore the autosave: {e}");
			return GDocument.New();
		}
	}
}

/// <summary>Writes autosave.glb every so often, and when the app loses focus,
/// if the mesh changed since the last autosave.</summary>
class GAutosave(GDocument doc)
{
	const double INTERVAL_SECONDS = 30;

	double lastTime = Time.Total;
	int lastHash = doc.mesh.ContentHash();

	/// <param name="now">Save now if anything changed, e.g. on quit.</param>
	public void Tick(GDocument doc, bool now)
	{
		if (!now && Time.Total - lastTime < INTERVAL_SECONDS) return;
		lastTime = Time.Total;

		int hash = doc.mesh.ContentHash();
		// Nothing new since the last autosave, or the work is saved in its own
		// file, so there's nothing to protect (and a newer autosave would be
		// restored on startup as unsaved work).
		if (hash == lastHash || hash == doc.savedHash) return;
		lastHash = hash;

		// The document's savedHash is about its real file, so autosave doesn't touch it.
		try { GFiles.Save(GFiles.PathFor(GFiles.AUTOSAVE_NAME), doc, null); }
		catch (IOException e) { Log.Err($"Autosave failed: {e.Message}"); }
		catch (UnauthorizedAccessException e) { Log.Err($"Autosave failed: {e.Message}"); }
	}
}
