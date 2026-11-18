using System;
using System.Collections.Generic;
using System.Linq;
using StereoKit;

namespace gesh;

enum GHomePage
{
	Gallery,
	Settings,
	Info,
}

/// <summary>A window over Home that has to be answered first.</summary>
abstract record GModal;
/// <summary>Asks before doing something that can't be undone.</summary>
/// <param name="act">Does it, and returns the document to use from then on.</param>
record GAsk(string text, string confirm, Func<GDocument, GDocument> act) : GModal;
/// <summary>Something went wrong.</summary>
record GTell(string text) : GModal;
/// <summary>The license notices of everything gesh ships.</summary>
record GNotices : GModal;

/// <summary>
/// Home: a window in the world for the rare things. The current model's name,
/// save and new along the top; its thumbnail and the page buttons on the
/// left; on the right the saved models as cards, or the settings, or the info
/// page. Opened from the palm menu's strip; opening a file or starting a new
/// one closes it. See plans/ui-design.md and plans/save.md.
/// </summary>
/// <param name="sounds">For a save finishing, and anything failing.</param>
class GHome(GSounds sounds)
{
	static readonly Vec2 SIZE = new(0.46f, 0.37f);
	const float LEFT = 0.12f;
	const float BUTTON = GPalmMenu.BUTTON;
	const float NEW = 0.07f;
	const float CARD = 0.093f;
	const float CARD_LABEL = 0.026f;
	const double LIST_REFRESH_SECONDS = 1;
	/// <summary>Dev tuning that won't ship, at the bottom of Settings.</summary>
	const bool EXPERIMENTAL = false;
	/// <summary>Modals sit this far in front of Home, toward the user (-z).</summary>
	const float MODAL_LIFT = 0.04f;
	/// <summary>Monospace columns the notices are wrapped to.</summary>
	const int NOTICE_COLUMNS = 92;
	const string WEBSITE = "https://graic.net";

	/// <summary>Where Home is, while it's open.</summary>
	Pose? pose;
	GHomePage page = GHomePage.Gallery;
	GModal? modal;
	/// <summary>The first row shown of each scrolled list.</summary>
	readonly GScroll cardScroll = new(), settingsScroll = new(), noticesScroll = new();

	List<GFileEntry> files = [];
	/// <summary>Why the saves folder couldn't be listed, if it couldn't.</summary>
	string? listError;
	double filesListedAt = double.MinValue;
	/// <summary>Thumbnails by path, along with the file time they're from.</summary>
	readonly Dictionary<string, (DateTime modified, Tex? tex)> thumbnails = new();
	/// <summary>Assets/notices.txt wrapped to lines, read when first shown.</summary>
	string[]? noticeLines;

	/// <summary>The Home button: opens Home, or closes it if it's open.</summary>
	public void Toggle()
	{
		if (pose.HasValue) Close();
		else Open();
	}

	/// <summary>Opens Home in front of the user, or brings it there.</summary>
	public void Open()
	{
		Pose head = Input.Head;
		Vec3 forward = head.Forward;
		forward = new Vec3(forward.x, 0, forward.z).Normalized;
		Vec3 at = head.position + forward * 0.55f - Vec3.Up * 0.1f;
		Quat facing = Quat.LookAt(at, new Vec3(head.position.x, at.y, head.position.z));
		// The pose is the window's top center; tilted back a little for reading.
		pose = new Pose(at + Vec3.Up * (SIZE.y / 2), facing * Quat.FromAngles(10, 0, 0));
		filesListedAt = double.MinValue;
	}

	void Close()
	{
		pose = null;
		modal = null;
	}

	/// <returns>The document to use from now on, a different one if a file
	/// was opened or a new one started.</returns>
	/// <param name="passthroughAvailable">The headset can show the room.</param>
	/// <param name="handMeshAvailable">The headset has Meta's hand mesh.</param>
	public GDocument Tick(GSettings settings, GDocument doc, GHints hints, bool passthroughAvailable, bool handMeshAvailable)
	{
		if (!pose.HasValue) return doc;

		if (Time.Total - filesListedAt > LIST_REFRESH_SECONDS)
		{
			filesListedAt = Time.Total;
			try
			{
				files = GFiles.List();
				listError = null;
			}
			catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
			{
				files = [];
				listError = $"Can't read the saves folder: {e.Message}";
			}
		}

		Pose p = pose.Value;
		UI.WindowBegin("gesh home", ref p, SIZE, UIWin.Body, UIMove.FaceUser);
		pose = p;
		// Everything under a modal waits for it.
		UI.PushEnabled(modal == null);
		// The open model's thumbnail, name, save and new are about files, so
		// they're only on the files page.
		bool filesPage = page == GHomePage.Gallery;
		if (filesPage) DrawThumbnail(doc);
		GDocument shown = filesPage ? DrawTop(doc) : doc;
		switch (page)
		{
			case GHomePage.Gallery: shown = DrawGallery(shown); break;
			case GHomePage.Settings: shown = DrawSettings(settings, shown, hints, passthroughAvailable, handMeshAvailable); break;
			case GHomePage.Info: DrawInfo(); break;
		}
		UI.PopEnabled();
		UI.WindowEnd();
		DrawTabs(p);

		if (modal != null) shown = DrawModal(p, shown);
		// A modal has its own way out.
		if (pose.HasValue && modal == null && GDraw.Close("gesh home close", GDraw.TopRight(p, SIZE.x))) Close();
		if (shown != doc) Close();
		return shown;
	}

	/// <summary>Right of the thumbnail: the open model's name, save in the
	/// top right corner, and new below save.</summary>
	GDocument DrawTop(GDocument doc)
	{
		string fileName = GFiles.SanitizeName(doc.name);
		bool exists = fileName != GFiles.AUTOSAVE_NAME && files.Any(f => SameName(f.name, fileName));
		UI.Input("name", ref doc.name, new Vec2(UI.LayoutRemaining.x - BUTTON - UI.Settings.gutter, BUTTON));
		UI.SameLine();
		// Save is blue while there's something to save.
		if (GTheme.Colored(doc.HasUnsavedChanges ? GTheme.ACCENT : null, () => UI.ButtonImg("save", GIcons.Get(GIcon.Save), UIBtnLayout.CenterNoText, new Vec2(BUTTON, BUTTON))))
		{
			if (exists) modal = new GAsk($"Are you sure you want to overwrite {fileName}?", "Overwrite", d => Save(d, fileName));
			else Save(doc, fileName);
		}
		UI.HSpace(UI.LayoutRemaining.x - NEW);
		if (UI.ButtonImg("New", GIcons.Get(GIcon.Add), UIBtnLayout.Left, new Vec2(NEW, BUTTON)))
		{
			if (doc.HasUnsavedChanges) modal = new GAsk("Are you sure you want to make a new file? Unsaved changes are lost", "New file", _ => GDocument.New());
			else doc = GDocument.New();
		}
		return doc;
	}

	/// <summary>Saves, and says so only if it failed: a save that worked
	/// shows in the thumbnail and the gallery.</summary>
	GDocument Save(GDocument doc, string fileName)
	{
		GSaver.SaveWithThumbnail(doc, () => GFiles.PathFor(fileName), error =>
		{
			if (error != null) Fail(error);
			else if (pose.HasValue) sounds.Saved(pose.Value.position);
			filesListedAt = double.MinValue;
		});
		return doc;
	}

	/// <summary>The left column of the files page: the open model's
	/// thumbnail in the top left corner, if it's been saved.</summary>
	void DrawThumbnail(GDocument doc)
	{
		UI.LayoutPushCut(UICut.Left, LEFT + UI.Settings.gutter);
		string fileName = GFiles.SanitizeName(doc.name);
		GFileEntry? saved = files.FirstOrDefault(f => SameName(f.name, fileName));
		Bounds thumb = UI.LayoutReserve(new Vec2(LEFT, LEFT));
		Tex? tex = saved != null ? Thumbnail(saved) : null;
		if (tex != null) GDraw.Texture(tex, thumb.center, new Vec2(LEFT, LEFT), Color.White);
		else
		{
			GDraw.Shape(GIcon.Square, thumb.center, new Vec2(LEFT, LEFT), new Color(0.15f, 0.16f, 0.17f).ToLinear());
			Text.Add("not saved yet", Matrix.T(thumb.center + new Vec3(0, 0, -GDraw.LIFT * 2)), GTheme.Small, Pivot.Center, Align.Center);
		}
		UI.LayoutPop();
	}

	static readonly (GHomePage page, GIcon icon)[] TABS = [(GHomePage.Gallery, GIcon.Files), (GHomePage.Settings, GIcon.Settings), (GHomePage.Info, GIcon.Info)];

	/// <summary>The page buttons, centered under the window with no
	/// background, like the palm menu's tools.</summary>
	/// <param name="window">The home window's top center.</param>
	void DrawTabs(Pose window)
	{
		Pose at = new(window.position - window.orientation * Vec3.Up * (SIZE.y + GPalmMenu.GAP), window.orientation);
		UI.WindowBegin("gesh home tabs", ref at, new Vec2(GPalmMenu.RowWidth(TABS.Length), GPalmMenu.StripHeight), UIWin.Empty, UIMove.None);
		UI.PushEnabled(modal == null);
		Vec2 button = new(GPalmMenu.STRIP_BUTTON, GPalmMenu.STRIP_BUTTON);
		foreach (var (pg, icon) in TABS)
		{
			Sprite sprite = GIcons.Get(icon);
			bool current = page == pg;
			if (GTheme.Colored(current ? GTheme.ACCENT : null, () => UI.Radio(pg.ToString(), current, sprite, sprite, UIBtnLayout.CenterNoText, button))) page = pg;
			UI.SameLine();
		}
		UI.PopEnabled();
		UI.WindowEnd();
	}

	/// <summary>Saved models as cards, newest first, scrolled by rows, and
	/// where they're kept along the bottom.</summary>
	GDocument DrawGallery(GDocument doc)
	{
		float footer = UI.LineHeight;
		Vec2 area = UI.LayoutRemaining - new Vec2(0, footer + UI.Settings.gutter);
		Vec3 top = UI.LayoutAt;

		if (files.Count == 0)
		{
			UI.Text(listError ?? "No saved files", Align.Center, TextFit.Wrap, area);
		}
		else
		{
			float rowHeight = CARD + CARD_LABEL + UI.Settings.gutter;
			int columns = 3;
			int rows = int.Max(1, (int)((area.y + UI.Settings.gutter) / (rowHeight + UI.Settings.gutter)));
			int totalRows = (files.Count + columns - 1) / columns;
			string openName = GFiles.SanitizeName(doc.name);
			float step = rowHeight + UI.Settings.gutter;
			float listHeight = rows * step - UI.Settings.gutter;
			Vec2 listSize = new(area.x - GScroll.BAR - UI.Settings.gutter, listHeight);
			cardScroll.Tick("cards", top, listSize, step, totalRows, rows);

			// Rows move smoothly with the scroll and are cut off at the list's
			// edges as they slide past.
			float position = cardScroll.Position;
			GClip.Push(top, listHeight);
			for (int r = int.Max(0, (int)MathF.Floor(position)); r < totalRows; r++)
			{
				float y = (r - position) * step;
				if (y >= listHeight) break;
				UI.LayoutPush(top - new Vec3(0, y, 0), new Vec2(listSize.x, rowHeight));
				var row = files.Skip(r * columns).Take(columns).ToList();
				// Thumbnails, each a button that opens its file.
				foreach (GFileEntry file in row)
				{
					Bounds b = UI.LayoutReserve(new Vec2(CARD, CARD));
					UI.ButtonBehavior(GDraw.TopLeft(b), new Vec2(CARD, CARD), "open:" + file.path, out float fingerOffset, out BtnState state, out BtnState focus);
					float lift = float.Max(0, UI.Settings.depth * 0.5f - fingerOffset);
					// Framed blue if it's the open file. The square shape is inset
					// in its quad, so it is drawn larger.
					if (SameName(file.name, openName)) GDraw.Shape(GIcon.Square, b.center, new Vec2(CARD + 0.005f, CARD + 0.005f) / 0.92f, GTheme.ACCENT.ToLinear(), lift: lift);
					Tex? tex = Thumbnail(file);
					float scale = focus.IsActive() ? 1 : 0.97f;
					if (tex != null) GDraw.Texture(tex, b.center, new Vec2(CARD, CARD) * scale, Color.White, layer: 1, lift: lift);
					else GDraw.Shape(GIcon.Square, b.center, new Vec2(CARD, CARD) * scale, new Color(0.15f, 0.16f, 0.17f).ToLinear(), layer: 1, lift: lift);
					if (state.IsJustActive() && modal == null)
					{
						if (doc.HasUnsavedChanges) modal = new GAsk($"Are you sure you want to open {file.name}? Unsaved changes are lost", "Open", d => TryOpen(file) ?? d);
						else doc = TryOpen(file) ?? doc;
					}
					UI.SameLine();
				}
				UI.NextLine();
				// Names and ages, each with its trash button.
				foreach (GFileEntry file in row)
				{
					UI.PushId(file.path);
					UI.PushTextStyle(GTheme.Small);
					UI.Text($"{file.name}\n{Ago(file.modified)}", Align.CenterLeft, TextFit.Clip, new Vec2(CARD - CARD_LABEL - UI.Settings.gutter, CARD_LABEL));
					UI.PopTextStyle();
					UI.SameLine();
					if (UI.ButtonImg("delete", GIcons.Get(GIcon.Delete), UIBtnLayout.CenterNoText, new Vec2(CARD_LABEL, CARD_LABEL)))
						modal = new GAsk($"Are you sure you want to delete {file.name}?", "Delete", d => { TryDelete(file); return d; });
					UI.PopId();
					UI.SameLine();
				}
				UI.LayoutPop();
			}
			GClip.Pop();
		}

		// Down to the footer, wherever the cards ended.
		float used = top.y - UI.LayoutAt.y;
		if (area.y - used > 0) UI.LayoutReserve(new Vec2(area.x, area.y - used - UI.Settings.gutter));
		UI.PushTextStyle(GTheme.Small);
		UI.Text($"Models saved to {GFiles.FolderPath()}", Align.Center, TextFit.Squeeze, new Vec2(area.x, footer));
		UI.PopTextStyle();
		return doc;
	}

	/// <summary>Settings, one per row, scrolled when they don't all fit.</summary>
	/// <returns>The document to use from now on: the tutorial's starting
	/// shape if it was started.</returns>
	GDocument DrawSettings(GSettings settings, GDocument doc, GHints hints, bool passthroughAvailable, bool handMeshAvailable)
	{
		GDocument result = doc;
		var rows = new List<Action<Vec2>>
		{
			// Starts over on the first launch's shape, which closes Home, so the
			// first hint over the model is in view.
			row =>
			{
				if (!UI.ButtonImg("Play tutorial", null, UIBtnLayout.None, row, Align.CenterLeft)) return;
				if (doc.HasUnsavedChanges) modal = new GAsk("Are you sure you want to play the tutorial? Unsaved changes are lost", "Play tutorial", _ => StartTutorial(hints));
				else result = StartTutorial(hints);
			},
			row => Toggle("Passthrough", ref settings.passthrough, row, passthroughAvailable),
			row => Slider(settings.soundVolume > 0 ? $"Sounds {settings.soundVolume * 100:0}%" : "Sounds off", "soundVolume", ref settings.soundVolume, 0, 1, row),
		};
		if (EXPERIMENTAL)
		{
			rows.Add(row =>
			{
				UI.PushTextStyle(GTheme.Small);
				UI.Text("Experimental", Align.BottomLeft, TextFit.Clip, row);
				UI.PopTextStyle();
			});
			rows.Add(row => Toggle("Meta hand mesh", ref settings.metaHandMesh, row, handMeshAvailable));
			rows.Add(row => Toggle("Mesh outline", ref settings.meshOutline, row, true));
			rows.Add(row => Slider("Reach", "grabRadius", ref settings.grabRadius, 0.01f, 0.15f, row));
			rows.Add(row => Toggle("Release from closest", ref settings.experimentalPinch, row, true));
			// Start and release limit each other's sliders: releasing closer
			// than starting would make pinches flicker.
			if (settings.experimentalPinch) rows.Add(row => Slider($"Release +{settings.pinchReleaseDelta * 100:0.0}cm", "pinchReleaseDelta", ref settings.pinchReleaseDelta, 0.002f, 0.03f, row));
			else rows.Add(row => Slider($"Release {settings.pinchEndGap * 100:0.0}cm", "pinchEnd", ref settings.pinchEndGap, settings.pinchStartGap, 0.05f, row));
			rows.Add(row => Slider($"Pinch {settings.pinchStartGap * 100:0.0}cm", "pinchStart", ref settings.pinchStartGap, 0.002f, settings.experimentalPinch ? 0.04f : settings.pinchEndGap, row));
			rows.Add(row => Slider($"Air pinch {settings.airPinchGap * 100:0.0}cm", "airPinch", ref settings.airPinchGap, 0.002f, settings.pinchStartGap, row));
			rows.Add(row => Slider($"Palm open {settings.menuStraightness:0.00}", "menuStraightness", ref settings.menuStraightness, 0.5f, 0.99f, row));
			rows.Add(row => Slider($"Outline size {settings.outlineScale * 100:0}%", "outlineScale", ref settings.outlineScale, 0.1f, 1.5f, row));
			rows.Add(row => Slider($"Turn smoothing {settings.turnSmoothing * 1000:0}ms", "turnSmoothing", ref settings.turnSmoothing, 0, 0.4f, row));
			rows.Add(row =>
			{
				float texel = doc.mesh.paint.texelSize * 1000;
				Slider($"Paint px {texel:0.0}mm", "texel", ref texel, 1, 10, row);
				// Snap to half millimeters, and only refit when it changes.
				float snapped = MathF.Round(texel * 2) / 2 / 1000;
				if (snapped != doc.mesh.paint.texelSize) doc.mesh.SetTexelSize(snapped);
			});
		}

		Vec3 top = UI.LayoutAt;
		Vec2 area = UI.LayoutRemaining;
		int shown = int.Max(1, (int)((area.y + UI.Settings.gutter) / (BUTTON + UI.Settings.gutter)));
		bool scrolls = rows.Count > shown;
		Vec2 size = new(area.x - (scrolls ? GScroll.BAR + UI.Settings.gutter : 0), BUTTON);
		float step = BUTTON + UI.Settings.gutter;
		float listHeight = shown * step - UI.Settings.gutter;
		settingsScroll.Tick("settings", top, new Vec2(size.x, listHeight), step, rows.Count, shown);
		// Rows move smoothly with the scroll, each laid out at its own
		// height, and are cut off at the list's edges as they slide past.
		float position = settingsScroll.Position;
		GClip.Push(top, listHeight);
		for (int i = int.Max(0, (int)MathF.Floor(position)); i < rows.Count; i++)
		{
			float y = (i - position) * step;
			if (y >= listHeight) break;
			UI.LayoutPush(top - new Vec3(0, y, 0), size);
			rows[i](size);
			UI.LayoutPop();
		}
		GClip.Pop();
		return result;
	}

	static GDocument StartTutorial(GHints hints)
	{
		GDocument start = GDocument.New();
		hints.Restart(start);
		return start;
	}

	static void Toggle(string label, ref bool value, Vec2 size, bool enabled)
	{
		UI.PushEnabled(enabled);
		UI.Toggle(label, ref value, size);
		UI.PopEnabled();
	}

	/// <summary>A label and its slider on one row.</summary>
	static void Slider(string label, string id, ref float value, float min, float max, Vec2 row)
	{
		// Fixed size text, so labels don't each shrink by a different amount.
		const float LABEL = 0.1f;
		UI.Text(label, Align.CenterLeft, TextFit.Clip, new Vec2(LABEL, row.y));
		UI.SameLine();
		GDraw.HSlider(id, ref value, min, max, UI.LayoutReserve(new Vec2(row.x - LABEL - UI.Settings.gutter, row.y)));
	}

	/// <summary>The app, its version, who made it, and the notices, centered
	/// in the window.</summary>
	void DrawInfo()
	{
		Version v = typeof(GHome).Assembly.GetName().Version ?? throw new InvalidOperationException("No assembly version");
		float width = UI.LayoutRemaining.x;
		float titleHeight = Text.SizeLayout("G", GTheme.Title).y;
		// Extra room above the button, so it doesn't crowd the credit.
		float buttonGap = UI.Settings.gutter * 4;
		float height = titleHeight + UI.Settings.gutter + UI.LineHeight + buttonGap + BUTTON;
		UI.VSpace(float.Max(0, (UI.LayoutRemaining.y - height) / 2));

		UI.PushTextStyle(GTheme.Title);
		// 0.1 or 0.1.1: the third number only when there is one.
		string shown = v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
		UI.Text($"Gesh v{shown}", Align.Center, TextFit.Clip, new Vec2(width, titleHeight));
		UI.PopTextStyle();

		// "Made with <3 by Graic", the name in bold and a link.
		const string BY = "Made with <3 by ";
		const string NAME = "Graic";
		float byWidth = Text.SizeLayout(BY, GTheme.Small).x;
		float nameWidth = Text.SizeLayout(NAME, GTheme.SmallBold).x;
		UI.HSpace((width - byWidth - nameWidth) / 2);
		Bounds line = UI.LayoutReserve(new Vec2(byWidth + nameWidth, UI.LineHeight));
		Vec3 left = GDraw.TopLeft(line) - new Vec3(0, line.dimensions.y / 2, GDraw.LIFT * 2);
		Text.Add(BY, Matrix.T(left), GTheme.Small, Pivot.CenterLeft, Align.CenterLeft);
		// Layout x grows to the user's left: the name is at the right end.
		Bounds link = new(left - new Vec3(byWidth + nameWidth / 2, 0, -GDraw.LIFT * 2), new Vec3(nameWidth, line.dimensions.y, 0));
		UI.ButtonBehavior(GDraw.TopLeft(link), link.dimensions.XY, "website", out _, out BtnState state, out BtnState focus);
		Color linkColor = focus.IsActive() ? Color.White : GTheme.ACCENT;
		Text.Add(NAME, Matrix.T(link.center + new Vec3(0, 0, -GDraw.LIFT * 2)), GTheme.SmallBold, linkColor.ToLinear(), Pivot.Center, Align.Center);
		if (state.IsJustActive()) GLink.Open(WEBSITE);

		const float NOTICES = 0.1f;
		UI.VSpace(buttonGap - UI.Settings.gutter);
		UI.HSpace((width - NOTICES) / 2);
		if (UI.Button("Copyright notices", new Vec2(NOTICES, BUTTON))) modal = new GNotices();
	}

	/// <summary>The modal, in front of Home's middle.</summary>
	GDocument DrawModal(Pose home, GDocument doc)
	{
		float message = UI.LineHeight * 2.4f;
		Vec2 size = modal is GNotices ? new Vec2(SIZE.x - 0.02f, SIZE.y - 0.02f) : new Vec2(0.24f, UI.Settings.margin * 2 + message + UI.Settings.gutter + BUTTON);
		Vec3 middle = home.position + home.orientation * new Vec3(0, -SIZE.y / 2 + size.y / 2, -MODAL_LIFT);
		Pose at = new(middle, home.orientation);
		// Lighter than Home, so it reads as on top of it.
		Color background = UI.GetThemeColor(UIColor.Background);
		UI.SetThemeColor(UIColor.Background, background * 1.45f);
		UI.WindowBegin("gesh modal", ref at, size, UIWin.Body, UIMove.None);
		switch (modal)
		{
			case GAsk ask:
				UI.Text(ask.text, Align.TopLeft, TextFit.Wrap, new Vec2(UI.LayoutRemaining.x, message));
				Vec2 button = new((UI.LayoutRemaining.x - UI.Settings.gutter) / 2, BUTTON);
				if (UI.Button("Cancel", button)) modal = null;
				UI.SameLine();
				if (GTheme.Colored(GTheme.DANGER, () => UI.Button(ask.confirm, button)))
				{
					// The act may itself report a problem in a new modal.
					modal = null;
					doc = ask.act(doc);
				}
				break;
			case GTell tell:
				UI.Text(tell.text, Align.TopLeft, TextFit.Wrap, new Vec2(UI.LayoutRemaining.x, message));
				if (UI.Button("OK", new Vec2(UI.LayoutRemaining.x, BUTTON))) modal = null;
				break;
			case GNotices:
				DrawNotices(size);
				break;
			default:
				throw new InvalidOperationException($"Unknown modal {modal}");
		}
		UI.WindowEnd();
		UI.SetThemeColor(UIColor.Background, background);
		// The notices are the one modal without a button to leave by.
		if (modal is GNotices && GDraw.Close("gesh notices close", GDraw.TopRight(at, size.x))) modal = null;
		return doc;
	}

	/// <summary>Every license notice, in monospace, scrolled by lines. Only
	/// the lines in view are drawn: the whole text is a few thousand lines.</summary>
	void DrawNotices(Vec2 size)
	{
		noticeLines ??= Wrap(Platform.ReadFileText("notices.txt") ?? throw new InvalidOperationException("Assets/notices.txt is missing"), NOTICE_COLUMNS);
		UI.Text("Copyright notices", Align.CenterLeft, TextFit.Clip, new Vec2(UI.LayoutRemaining.x, BUTTON));

		Vec3 top = UI.LayoutAt;
		Vec2 area = UI.LayoutRemaining;
		float line = Text.SizeLayout("A", GTheme.Mono).y;
		int shown = (int)(area.y / line);
		Vec2 textSize = new(area.x - GScroll.BAR - UI.Settings.gutter, area.y);
		noticesScroll.Tick("notices", top, textSize, line, noticeLines.Length, shown);
		// The lines move smoothly with the scroll and are cut off at the
		// list's edges: two more lines are drawn, shifted up by the fraction.
		float position = noticesScroll.Position;
		int first = (int)MathF.Floor(position);
		string text = string.Join('\n', noticeLines, first, int.Min(shown + 2, noticeLines.Length - first));
		GClip.Push(top, area.y);
		UI.PushTextStyle(GTheme.Mono);
		Vec2 linesSize = new(textSize.x, (shown + 2) * line);
		UI.LayoutPush(top + new Vec3(0, (position - first) * line, 0), linesSize);
		UI.Text(text, Align.TopLeft, TextFit.Clip, linesSize);
		UI.LayoutPop();
		UI.PopTextStyle();
		GClip.Pop();
	}

	/// <summary>The text's lines, long ones broken at spaces to fit the columns.</summary>
	static string[] Wrap(string text, int columns)
	{
		var lines = new List<string>();
		foreach (string raw in text.Replace("\r", "").Split('\n'))
		{
			string rest = raw.Replace("\t", "    ");
			while (rest.Length > columns)
			{
				int cut = rest.LastIndexOf(' ', columns);
				if (cut <= 0) cut = columns;
				lines.Add(rest[..cut]);
				rest = rest[cut..].TrimStart();
			}
			lines.Add(rest);
		}
		return [.. lines];
	}

	/// <summary>File names don't care about case on Windows or the Quest's storage.</summary>
	static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	static string Ago(DateTime utc)
	{
		TimeSpan age = DateTime.UtcNow - utc;
		if (age.TotalMinutes < 1) return "just now";
		if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
		if (age.TotalDays < 1) return $"{(int)age.TotalHours} h ago";
		return utc.ToLocalTime().ToString("MMM d");
	}

	void TryDelete(GFileEntry file)
	{
		try { System.IO.File.Delete(file.path); }
		catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
		{
			Fail($"Couldn't delete {file.name}: {e.Message}");
		}
		filesListedAt = double.MinValue;
	}

	/// <summary>Tells the user something went wrong, with the failure sound.</summary>
	void Fail(string message)
	{
		modal = new GTell(message);
		if (pose.HasValue) sounds.Failed(pose.Value.position);
	}

	GDocument? TryOpen(GFileEntry file)
	{
		try { return GFiles.Load(file.path); }
		catch (Exception e)
		{
			// Files are outside input: broken, hand edited, or from other
			// programs. That's not a bug in gesh, so it's reported, not thrown.
			Fail($"Couldn't open {file.name}: {e.Message}");
			Log.Warn($"Couldn't open {file.path}: {e}");
			return null;
		}
	}

	Tex? Thumbnail(GFileEntry file)
	{
		if (thumbnails.TryGetValue(file.path, out var cached) && cached.modified == file.modified) return cached.tex;

		Tex? tex = null;
		try
		{
			byte[]? png = GFiles.ReadThumbnail(file.path);
			if (png != null) tex = Tex.FromMemory(png);
		}
		catch (Exception e)
		{
			// Same as opening: a broken file just has no thumbnail.
			Log.Warn($"No thumbnail for {file.path}: {e.Message}");
		}
		thumbnails[file.path] = (file.modified, tex);
		return tex;
	}
}
