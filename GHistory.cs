using System;
using System.Collections.Generic;
using System.Linq;

namespace gesh;

/// <summary>
/// Undo and redo for one document: a single queue of steps, mesh edits and
/// painting alike, in the order they happened. Moving the world isn't part
/// of it. See plans/undo.md.
///
/// A step keeps before and after images, not inverse operations: a snapshot
/// of the mesh structure (cheap), and the paint tiles the step wrote to, as
/// they were before and after. The atlas reports every write before it
/// happens, so tiles are captured on first touch. The caller says when a step
/// ends (Commit), which is whenever the hands stop acting.
/// </summary>
class GHistory
{
	/// <summary>Older steps are dropped beyond this much memory.</summary>
	const long BUDGET_BYTES = 64L << 20;

	sealed record Step(GEditableMesh.Snapshot before, GEditableMesh.Snapshot after, Dictionary<(int page, int tile), byte[]> beforeTiles, Dictionary<(int page, int tile), byte[]> afterTiles)
	{
		public long Bytes => (beforeTiles.Count + afterTiles.Count) * (long)GPaintAtlas.TILE * GPaintAtlas.TILE * 4
			+ (before.vertices.values.Count + before.edges.values.Count + before.faces.values.Count) * 64L * 2;
	}

	readonly GEditableMesh mesh;
	readonly List<Step> undo = [];
	readonly List<Step> redo = [];
	/// <summary>The mesh as of the last step, and its content hash.</summary>
	GEditableMesh.Snapshot current;
	int currentHash;
	/// <summary>Tiles written since the last step, as they were before.</summary>
	Dictionary<(int page, int tile), byte[]> pending = [];

	public GHistory(GEditableMesh mesh)
	{
		this.mesh = mesh;
		current = mesh.TakeSnapshot();
		currentHash = mesh.ContentHash();
		if (mesh.paint.beforeWrite != null) throw new InvalidOperationException("The mesh already has a history");
		mesh.paint.beforeWrite = Capture;
	}

	public bool CanUndo => undo.Count > 0 || pending.Count > 0;
	public bool CanRedo => redo.Count > 0;
	public int UndoCount => undo.Count;

	void Capture(int page, int x, int y, int width, int height)
	{
		foreach (int tile in GPaintAtlas.TilesIn(x, y, width, height))
		{
			if (!pending.ContainsKey((page, tile))) pending[(page, tile)] = mesh.paint.ReadTile(page, tile);
		}
	}

	/// <summary>Ends the current step: whatever changed since the last one
	/// becomes one undoable step. Nothing changed, no step.</summary>
	public void Commit()
	{
		int hash = mesh.ContentHash();
		if (hash == currentHash)
		{
			pending.Clear();
			return;
		}
		GEditableMesh.Snapshot after = mesh.TakeSnapshot();
		Dictionary<(int, int), byte[]> afterTiles = pending.Keys.ToDictionary(k => k, k => mesh.paint.ReadTile(k.page, k.tile));
		undo.Add(new Step(current, after, pending, afterTiles));
		redo.Clear();
		current = after;
		currentHash = hash;
		pending = [];

		// Drop the oldest steps over budget, but always keep the newest.
		long total = undo.Sum(s => s.Bytes);
		while (undo.Count > 1 && total > BUDGET_BYTES)
		{
			total -= undo[0].Bytes;
			undo.RemoveAt(0);
		}
	}

	/// <returns>Whether there was anything to undo.</returns>
	public bool Undo()
	{
		Commit();
		if (undo.Count == 0) return false;
		Step step = undo[^1];
		undo.RemoveAt(undo.Count - 1);
		Apply(step.before, step.beforeTiles);
		redo.Add(step);
		return true;
	}

	/// <returns>Whether there was anything to redo.</returns>
	public bool Redo()
	{
		// A change since the last undo is a new step, which ends redo.
		Commit();
		if (redo.Count == 0) return false;
		Step step = redo[^1];
		redo.RemoveAt(redo.Count - 1);
		Apply(step.after, step.afterTiles);
		undo.Add(step);
		return true;
	}

	void Apply(GEditableMesh.Snapshot snapshot, Dictionary<(int page, int tile), byte[]> tiles)
	{
		foreach (var ((page, tile), pixels) in tiles) mesh.paint.WriteTile(page, tile, pixels);
		mesh.Restore(snapshot);
		current = snapshot;
		currentHash = mesh.ContentHash();
	}
}
