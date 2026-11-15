using System;
using System.IO;
using System.Text.Json;
using StereoKit;

namespace gesh;

/// <summary>Keeps GSettings in settings.json next to the saves, so they
/// carry over between runs. Fields missing from the file (new settings) keep
/// their defaults.</summary>
class GSettingsFile
{
	/// <summary>How often to check whether the settings changed.</summary>
	const double CHECK_SECONDS = 1;
	static readonly JsonSerializerOptions OPTIONS = new() { IncludeFields = true, WriteIndented = true };

	string saved;
	double checkedAt = double.NegativeInfinity;
	/// <summary>The last write's failure, while writes are failing.</summary>
	string? writeError;

	public GSettingsFile(GSettings settings)
	{
		saved = JsonSerializer.Serialize(settings, OPTIONS);
	}

	static string FilePath() => Path.Combine(GFiles.Folder(), "settings.json");

	/// <summary>The saved settings, or the defaults if there are none. A file
	/// that can't be read (cut off by a crash mid-write, or edited by hand)
	/// is reported and replaced by the defaults.</summary>
	public static GSettings Load()
	{
		string path = FilePath();
		if (!File.Exists(path)) return new GSettings();
		try
		{
			return JsonSerializer.Deserialize<GSettings>(File.ReadAllText(path), OPTIONS) ?? throw new JsonException("null settings");
		}
		catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
		{
			Log.Warn($"Ignoring unreadable {path}: {e.Message}");
			return new GSettings();
		}
	}

	/// <summary>Writes the settings once in a while, if they changed. A
	/// failed write (a full disk) is logged and tried again later.</summary>
	/// <param name="now">Write now, not on the next check: the app is closing.</param>
	public void Tick(GSettings settings, bool now = false)
	{
		if (!now && Time.Total - checkedAt < CHECK_SECONDS && SK.AppFocus == AppFocus.Active) return;
		checkedAt = Time.Total;
		string json = JsonSerializer.Serialize(settings, OPTIONS);
		if (json == saved) return;
		// Write then rename, so a crash mid-write leaves the old file.
		string path = FilePath();
		try
		{
			File.WriteAllText(path + ".tmp", json);
			File.Move(path + ".tmp", path, overwrite: true);
			saved = json;
			writeError = null;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			// Once per problem, not once a second while it lasts.
			if (e.Message != writeError) Log.Warn($"Couldn't save the settings: {e.Message}");
			writeError = e.Message;
		}
	}
}
