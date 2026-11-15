using System;
using StereoKit;

namespace gesh;

/// <summary>
/// The app's sound effects, see plans/sound.md. Each sound is computed
/// sample by sample once at startup, nothing is recorded. Many actions share
/// StereoKit's own UI sounds (its window grab and let-go thumps); menu
/// buttons already play its press and release. Sounds play where the action
/// is, so two hands sound like two hands.
///
/// The hands report what they did (GHandEvents); everything else that makes
/// a sound is a change from one tick to the next, which this notices itself:
/// the palm menu showing, the quick palette opening, an air pinch starting,
/// the zoom passing a step.
/// </summary>
class GSounds
{
	const int RATE = 48000;
	const float TAU = MathF.PI * 2;
	/// <summary>Short sounds vary their pitch by up to this much each time,
	/// so repeats don't grate. Not the chimes: they're notes.</summary>
	const float JITTER = 0.05f;
	/// <summary>The zoom clicks each time the scale passes another factor of
	/// this: steps of 5% at any zoom.</summary>
	const float ZOOM_STEP = 1.05f;

	readonly Sound grab = Sound.Find("default/sound_grab");
	readonly Sound letGo = Sound.Find("default/sound_ungrab");
	readonly Sound click = Sound.Find("default/sound_click");
	readonly Sound pop = Sound.FromSamples(Render(0.06f, Pop()));
	readonly Sound merge = Sound.FromSamples(Render(0.11f, Merge()));
	readonly Sound menuUp = Sound.FromSamples(Render(0.47f, MenuChime(up: true)));
	readonly Sound menuDown = Sound.FromSamples(Render(0.47f, MenuChime(up: false)));
	readonly Sound saved = Sound.FromSamples(Render(0.24f, Saved()));
	readonly Sound failed = Sound.FromSamples(Render(0.23f, Failed()));
	readonly Sound brush = Sound.FromSamples(BrushLoop());
	readonly Random random = new();

	/// <summary>Last tick's state, to hear what changed.</summary>
	bool menuShown;
	Vec3 menuAt;
	readonly (Vec3? center, (bool, int)? hovered)[] palettes = new (Vec3?, (bool, int)?)[2];
	readonly bool[] airPinching = new bool[2];
	/// <summary>Which step each scale was on, while it shows: the world's
	/// zoom, and a two-hand grab's.</summary>
	int? zoomStep, grabStep;
	readonly BrushVoice?[] brushes = new BrushVoice?[2];

	/// <summary>One hand's brush: a looping sound, its loudness following
	/// how fast the fingertip moves while it touches.</summary>
	class BrushVoice(SoundInst inst, Vec3 at)
	{
		// A struct handle, so not readonly: setting its volume writes through it.
		public SoundInst inst = inst;
		public Vec3 last = at;
		public float speed;
		public float volume;
	}

	public GSounds()
	{
		foreach (Sound s in new[] { grab, letGo, click })
			if (s == null) throw new InvalidOperationException("StereoKit's default sounds aren't loaded");
	}

	void Play(Sound sound, Vec3 at, bool jitter = true) =>
		sound.Play(at, new SoundPlay { pitch = jitter ? 1 + JITTER * (2 * random.NextSingle() - 1) : 1 });

	/// <summary>The hand's events, which are in mesh space.</summary>
	public void Hand(GHandEvents e, Matrix toWorld)
	{
		void At(Vec3? p, Sound s) { if (p.HasValue) Play(s, toWorld.Transform(p.Value)); }
		At(e.grabbed, grab);
		At(e.released, letGo);
		At(e.merged, merge);
		// One pop for making, deleting, filling and picking.
		At(e.extended, pop);
		At(e.deleted, pop);
		At(e.filled, pop);
		At(e.picked, pop);
	}

	/// <summary>A soft rising chime when the palm menu shows, falling when it
	/// goes away.</summary>
	/// <param name="at">Where the menu is, while it shows.</param>
	public void Menu(bool shown, Vec3 at)
	{
		if (shown) menuAt = at;
		if (shown != menuShown) Play(shown ? menuUp : menuDown, menuAt, jitter: false);
		menuShown = shown;
	}

	/// <summary>The quick palette: a thump when it opens, a click each time
	/// the hand points at another color, the let-go when it closes.</summary>
	public void Palette(Handed handed, GQuickPalette palette)
	{
		var (center, hovered) = palettes[(int)handed];
		if (palette.Center.HasValue && !center.HasValue) Play(grab, palette.Center.Value);
		else if (palette.Center.HasValue && palette.Pointed.HasValue && palette.Pointed != hovered) Play(click, palette.Center.Value);
		if (!palette.Center.HasValue && center.HasValue) Play(letGo, center.Value);
		palettes[(int)handed] = (palette.Center, palette.Pointed);
	}

	/// <summary>Moving the world: a thump when a hand's air pinch starts
	/// moving it, the let-go when it stops. With both hands, the zoom's scale
	/// clicks, see Scale.</summary>
	public void Air(Pose? left, Pose? right, float scale)
	{
		Pose?[] hands = [left, right];
		for (int h = 0; h < 2; h++)
		{
			bool pinching = hands[h].HasValue;
			if (pinching && !airPinching[h]) Play(grab, hands[h]!.Value.position);
			if (!pinching && airPinching[h]) Play(letGo, Input.Hand((Handed)h).palm.position);
			airPinching[h] = pinching;
		}
		Scale(ref zoomStep, left.HasValue && right.HasValue ? scale : null, left, right);
	}

	/// <summary>Both hands holding the same thing: its scale clicks.</summary>
	public void Grab(bool active, float scale, Pose left, Pose right) => Scale(ref grabStep, active ? scale : null, left, right);

	/// <summary>Wherever a scale shows between the hands, StereoKit's button
	/// click each time it passes another step, between the hands.</summary>
	/// <param name="scale">The scale, null while it isn't showing.</param>
	void Scale(ref int? last, float? scale, Pose? left, Pose? right)
	{
		if (!scale.HasValue)
		{
			last = null;
			return;
		}
		int step = (int)MathF.Floor(MathF.Log(scale.Value) / MathF.Log(ZOOM_STEP));
		if (last.HasValue && step != last) Play(click, Vec3.Lerp(left!.Value.position, right!.Value.position, 0.5f));
		last = step;
	}

	/// <summary>The brush's sound while it touches the mesh, louder and a
	/// little brighter the faster the fingertip moves, fading out when it
	/// lifts off.</summary>
	/// <param name="tip">The fingertip, in world space.</param>
	public void Brush(Handed handed, bool touching, Vec3 tip)
	{
		BrushVoice? v = brushes[(int)handed];
		if (v == null)
		{
			if (!touching) return;
			v = new BrushVoice(brush.Play(tip, new SoundPlay { volume = 0.0001f, flags = SoundFlags.Loop }), tip);
			brushes[(int)handed] = v;
		}
		float dt = Time.Stepf;
		if (dt <= 0) return;
		// The fingertip's speed in meters a second, smoothed over a few frames.
		float now = (tip - v.last).Length / dt;
		v.speed += (now - v.speed) * (1 - MathF.Exp(-dt / 0.05f));
		v.last = tip;
		float target = touching ? MathF.Min(1, v.speed / 1.0f) * 0.5f : 0;
		v.volume += (target - v.volume) * (1 - MathF.Exp(-dt / 0.04f));
		v.inst.Volume = MathF.Max(v.volume, 0.0001f);
		v.inst.Pitch = 0.85f + 0.35f * MathF.Min(1, v.speed / 1.5f);
		v.inst.Position = tip;
	}

	/// <summary>A save finished: two notes up.</summary>
	public void Saved(Vec3 at) => Play(saved, at, jitter: false);
	/// <summary>A save, open or delete failed: two notes down.</summary>
	public void Failed(Vec3 at) => Play(failed, at, jitter: false);

	#region The sounds

	static float[] Render(float seconds, Func<float, float> sample)
	{
		var samples = new float[(int)(seconds * RATE)];
		for (int i = 0; i < samples.Length; i++) samples[i] = sample((float)i / RATE);
		return samples;
	}

	/// <summary>x(1-x)^n over the sound, scaled so its peak is 1: a fast
	/// rise and a smooth fall. Zero outside 0 to 1.</summary>
	static float Env(float x, float n)
	{
		if (x <= 0 || x >= 1) return 0;
		float p = 1 / (n + 1);
		return x * MathF.Pow(1 - x, n) / (p * MathF.Pow(1 - p, n));
	}

	/// <summary>A few sines with x(1-x)^n envelopes: a short soft click.</summary>
	static float Click(float t, float length, (float hz, float amp, float n)[] partials)
	{
		float sum = 0;
		foreach (var (hz, amp, n) in partials) sum += MathF.Sin(TAU * hz * t) * Env(t / length, n) * amp;
		return sum;
	}

	/// <summary>One-pole low-pass, keeping its state between samples.</summary>
	class LowPass
	{
		float y;
		public float Next(float x, float hz) => y += (1 - MathF.Exp(-TAU * hz / RATE)) * (x - y);
	}

	/// <summary>A sine whose frequency changes, keeping its phase.</summary>
	class Sweep
	{
		float phase;
		public float Next(float hz) { phase += TAU * hz / RATE; return MathF.Sin(phase); }
	}

	/// <summary>A soft pop: a sine rising 260 to 900 Hz over 55 ms.</summary>
	static Func<float, float> Pop()
	{
		var s = new Sweep();
		return t => { float u = t / 0.055f; return 0.5f * s.Next(260 + 640 * u * u) * Env(u, 4); };
	}

	/// <summary>A snap: a click, then a blip rising 600 to 1300 Hz.</summary>
	static Func<float, float> Merge()
	{
		var s = new Sweep();
		return t =>
		{
			float clickPart = 0.45f * Click(t, 0.04f, [(380, 1, 8), (760, 0.3f, 12)]);
			float u = (t - 0.025f) / 0.07f;
			float blip = u > 0 && u < 1 ? 0.3f * s.Next(600 + 700 * u) * Env(u, 3) : 0;
			return clickPart + blip;
		};
	}

	/// <summary>The palm menu's glass chime: D4 and the A4 above it, 110 ms
	/// apart (the other way round going away), each a pair of sines slightly
	/// out of tune so they slowly beat, softly in and with a long tail,
	/// darkened by a low-pass at 704 Hz.</summary>
	static Func<float, float> MenuChime(bool up)
	{
		const float D4 = 293.66f, A4 = 440f, GAP = 0.11f, LENGTH = 0.34f, LEVEL = 0.38f, CUTOFF = 704;
		float[] notes = up ? [D4, A4] : [A4, D4];
		LowPass a = new(), b = new();
		return t =>
		{
			float x = 0;
			for (int k = 0; k < notes.Length; k++)
			{
				float local = t - k * GAP, u = local / LENGTH;
				if (u > 0 && u < 1) x += (MathF.Sin(TAU * notes[k] * local) + MathF.Sin(TAU * notes[k] * 1.004f * local)) * 0.5f * Env(u, 1.6f);
			}
			return b.Next(a.Next(x * LEVEL, CUTOFF), CUTOFF);
		};
	}

	/// <summary>Two notes up, G5 then D6, each with its octave.</summary>
	static Func<float, float> Saved() => t =>
	{
		float Note(float hz, float start) => (MathF.Sin(TAU * hz * t) + 0.3f * MathF.Sin(TAU * hz * 2 * t)) * Env((t - start) / 0.17f, 4);
		return 0.28f * (Note(784, 0) + Note(1175, 0.07f));
	};

	/// <summary>Two notes down, B4 then F#4.</summary>
	static Func<float, float> Failed() => t =>
	{
		float Note(float hz, float start) => (MathF.Sin(TAU * hz * t) + 0.2f * MathF.Sin(TAU * hz * 3 * t)) * Env((t - start) / 0.1f, 2);
		return 0.3f * (Note(494, 0) + Note(370, 0.11f));
	};

	/// <summary>The brush: two seconds of steady noise between steep cuts at
	/// 600 Hz and 1.2 kHz, like a muted felt marker. Its end fades into its
	/// start so it loops without a click, at a fixed loudness.</summary>
	static float[] BrushLoop()
	{
		const float SECONDS = 2, FADE = 0.4f, LOW = 600, HIGH = 1200, RMS = 0.3f;
		int n = (int)(SECONDS * RATE), f = (int)(FADE * RATE);
		var noise = new Random(24);
		LowPass[] highs = [new(), new()], lows = [new(), new(), new()];
		var raw = new float[n + f];
		for (int i = 0; i < raw.Length; i++)
		{
			float x = 2 * noise.NextSingle() - 1;
			foreach (LowPass hp in highs) x -= hp.Next(x, LOW);
			foreach (LowPass lp in lows) x = lp.Next(x, HIGH);
			raw[i] = x;
		}
		var loop = new float[n];
		double sum = 0;
		for (int i = 0; i < n; i++)
		{
			loop[i] = i < f ? raw[i] * ((float)i / f) + raw[n + i] * (1 - (float)i / f) : raw[i];
			sum += loop[i] * loop[i];
		}
		float scale = RMS / (float)System.Math.Sqrt(sum / n);
		for (int i = 0; i < n; i++) loop[i] *= scale;
		return loop;
	}

	#endregion
}
