using System.Collections.Generic;
using UnityEngine;

// Effets sonores et ambiances synthétisés au lancement (aucun fichier audio) : oscillateurs, bruit, filtres
// et enveloppes. Rendu volontairement rétro et un peu sale, dans l'esprit horreur low-poly du jeu.
public static class SoundBank
{
    const int Rate = 22050;
    const float TwoPi = Mathf.PI * 2f;

    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

    public static readonly string[] All =
    {
        "button", "door", "tick", "tock", "fanfare", "gameover", "laser_on", "laser_hum", "laser_hit",
        "hurt", "death", "revive", "duck", "pop", "eat", "key", "beep", "error", "register",
        "hub_music", "maze_drone", "insects", "neon", "blink", "step_tile", "step_dirt",
        "bell", "abjuration_music", "postit", "orb_on", "orb_break", "zone_door",
        "dame_scream", "dame_attack",
    };

    public static void Preload()
    {
        foreach (string name in All) Get(name);
    }

    static GameSounds recorded;

    public static AudioClip Get(string name)
    {
        if (clips.TryGetValue(name, out var clip) && clip != null) return clip;
        // Un son enregistré (Assets/Sounds) remplace le son synthétisé de même clé.
        if (recorded == null) recorded = Resources.Load<GameSounds>("GameSounds");
        clip = recorded != null ? recorded.Find(name) : null;
        if (clip != null) return clips[name] = clip;
        float[] data = Build(name, new System.Random(name.GetHashCode()));
        if (data == null) return null;
        Normalize(data, 0.9f);
        clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clips[name] = clip;
    }

    static float[] Build(string name, System.Random rnd)
    {
        switch (name)
        {
            case "button": return Button();
            case "door": return Door(rnd);
            case "tick": return Click(rnd, 3200f, 1800f, 0.06f);
            case "tock": return Click(rnd, 1400f, 900f, 0.08f);
            case "fanfare": return Fanfare(rnd);
            case "gameover": return GameOver();
            case "laser_on": return LaserOn(rnd);
            case "laser_hum": return LaserHum(rnd);
            case "laser_hit": return LaserHit(rnd);
            case "hurt": return Hurt(rnd);
            case "death": return Death(rnd);
            case "revive": return Revive();
            case "duck": return Duck(rnd);
            case "pop": return Pop();
            case "eat": return Eat(rnd);
            case "key": return Click(rnd, 2600f, 1200f, 0.045f);
            case "beep": return Beep(880f, 0.12f);
            case "error": return ErrorBeep();
            case "register": return Register(rnd);
            case "hub_music": return HubMusic(rnd);
            case "maze_drone": return MazeDrone(rnd);
            case "insects": return Insects(rnd);
            case "neon": return Neon(rnd);
            case "blink": return Blink(rnd);
            case "step_tile": return StepTile(rnd);
            case "step_dirt": return StepDirt(rnd);
            case "bell": return Bell(rnd);
            case "abjuration_music": return AbjurationMusic(rnd);
            case "postit": return PostIt(rnd);
            case "orb_on": return OrbOn(rnd);
            case "orb_break": return OrbBreak(rnd);
            case "zone_door": return ZoneDoor(rnd);
            case "dame_scream": return DameScream(rnd);
            case "dame_attack": return DameAttack(rnd);
        }
        return null;
    }

    // ---------- Outils ----------

    static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];
    static float Noise(System.Random rnd) => (float)rnd.NextDouble() * 2f - 1f;
    static float Square(float phase) => Mathf.Repeat(phase, 1f) < 0.5f ? 1f : -1f;
    static float Saw(float phase) => Mathf.Repeat(phase, 1f) * 2f - 1f;
    static float Sine(float phase) => Mathf.Sin(phase * TwoPi);
    static float Decay(float t, float time) => Mathf.Exp(-t / time);
    // Attaque linéaire puis décroissance exponentielle.
    static float AD(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);
    // Montée, palier puis relâchement sur une durée totale.
    static float ASR(float t, float attack, float release, float length) =>
        t < 0f || t > length ? 0f : Mathf.Min(1f, t / attack) * Mathf.Clamp01((length - t) / release);
    static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

    class LowPass
    {
        float y;
        public float Process(float x, float cutoff)
        {
            y += (1f - Mathf.Exp(-TwoPi * cutoff / Rate)) * (x - y);
            return y;
        }
    }

    // Filtre à variables d'état (passe-bande résonant, 'damping' petit = très résonant).
    class BandPass
    {
        float low, band;
        public float Process(float x, float cutoff, float damping)
        {
            float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(cutoff, Rate * 0.2f) / Rate);
            low += f * band;
            float high = x - low - damping * band;
            band += f * high;
            return band;
        }
    }

    static void Normalize(float[] data, float peak)
    {
        float max = 0f;
        foreach (float s in data) max = Mathf.Max(max, Mathf.Abs(s));
        if (max < 1e-5f) return;
        float k = peak / max;
        for (int i = 0; i < data.Length; i++) data[i] *= k;
    }

    // Boucle sans couture : la fin du tampon (plus long que 'loopSeconds') est fondue dans le début.
    static float[] MakeLoop(float[] longer, float loopSeconds, float fadeSeconds)
    {
        int length = Mathf.RoundToInt(loopSeconds * Rate), fade = Mathf.RoundToInt(fadeSeconds * Rate);
        var loop = new float[length];
        for (int i = 0; i < length; i++)
        {
            float s = longer[i];
            if (i < fade && length + i < longer.Length)
            {
                float k = (float)i / fade;
                s = s * k + longer[length + i] * (1f - k);
            }
            loop[i] = s;
        }
        return loop;
    }

    // ---------- Hub et manches ----------

    // Gros buzzer de cirque.
    static float[] Button()
    {
        var d = Buffer(0.75f);
        var lp = new LowPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float s = Square(98f * t) * 0.5f + Square(103.5f * t) * 0.4f + Saw(196.5f * t) * 0.2f;
            d[i] = lp.Process(s, 2400f) * ASR(t, 0.01f, 0.15f, 0.72f);
        }
        return d;
    }

    // Porte métallique qui grince en montant, puis s'arrête dans un choc sourd.
    static float[] Door(System.Random rnd)
    {
        var d = Buffer(1.7f);
        var creak = new BandPass();
        var grind = new BandPass();
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float rate = 32f + 14f * Mathf.Sin(TwoPi * 0.7f * t) + Noise(rnd) * 6f;
            phase += rate / Rate;
            float impulse = 0f;
            if (phase >= 1f) { phase -= 1f; impulse = 1f; }
            float s = creak.Process(impulse, 820f + 220f * Mathf.Sin(TwoPi * 0.45f * t), 0.04f) * 0.9f;
            s += grind.Process(Noise(rnd), 480f, 0.6f) * 0.25f;
            s += Sine(46f * t) * 0.25f;
            s *= ASR(t, 0.05f, 0.25f, 1.38f);
            if (t > 1.36f)
            {
                float k = t - 1.36f;
                s += Sine((72f - 30f * k) * k) * Decay(k, 0.12f) * 0.9f;
            }
            d[i] = s;
        }
        return d;
    }

    // Tic-tac, touche de clavier : petit clic résonant.
    static float[] Click(System.Random rnd, float noiseFreq, float toneFreq, float length)
    {
        var d = Buffer(length);
        var bp = new BandPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            d[i] = bp.Process(Noise(rnd), noiseFreq, 0.3f) * Decay(t, 0.006f) + Sine(toneFreq * t) * Decay(t, 0.012f) * 0.6f;
        }
        return d;
    }

    // Fanfare de cirque désaccordée (quota validé).
    static float[] Fanfare(System.Random rnd)
    {
        var d = Buffer(2.5f);
        (int note, float start, float length)[] melody =
        {
            (67, 0f, 0.15f), (72, 0.16f, 0.15f), (76, 0.32f, 0.15f), (79, 0.48f, 0.5f),
            (76, 1.0f, 0.15f), (79, 1.16f, 1f),
        };
        foreach (var (note, start, length) in melody)
        {
            float detune = 1f + Noise(rnd) * 0.025f;
            float f = Midi(note) * detune;
            var lp = new LowPass();
            int i0 = Mathf.RoundToInt(start * Rate), n = Mathf.RoundToInt((length + 0.1f) * Rate);
            for (int i = 0; i < n && i0 + i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float vib = 1f + 0.008f * Mathf.Sin(TwoPi * 6f * t);
                float s = Saw(f * vib * t) + Saw(f * 1.012f * vib * t) * 0.7f + Square(f * 0.5f * t) * 0.3f;
                d[i0 + i] += lp.Process(s, 3000f) * ASR(t, 0.01f, 0.08f, length + 0.1f) * 0.5f;
            }
        }
        // Coup de cymbale sur la note tenue.
        var hp = new LowPass();
        int c0 = Mathf.RoundToInt(0.48f * Rate);
        for (int i = c0; i < d.Length; i++)
        {
            float t = (float)(i - c0) / Rate;
            float x = Noise(rnd);
            d[i] += (x - hp.Process(x, 5000f)) * Decay(t, 0.35f) * 0.35f;
        }
        return d;
    }

    // Accord grave et dissonant qui s'effondre (game over).
    static float[] GameOver()
    {
        var d = Buffer(3.4f);
        float[] voices = { 110f, 116.5f, 164.8f, 55f };
        var lp = new LowPass();
        float[] phases = new float[voices.Length];
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float glide = Mathf.Lerp(1f, 0.72f, Mathf.SmoothStep(0f, 1f, t / 3.2f));
            float s = 0f;
            for (int v = 0; v < voices.Length; v++)
            {
                phases[v] += voices[v] * glide / Rate;
                s += Saw(phases[v]) * (v == 3 ? 0.6f : 0.4f);
            }
            s *= 0.75f + 0.25f * Mathf.Sin(TwoPi * 5f * t);
            s = lp.Process(s, Mathf.Lerp(1400f, 250f, t / 3.4f)) * ASR(t, 0.05f, 1.2f, 3.4f);
            s += Sine(55f * t - 8f * t * t) * Decay(t, 0.7f) * 0.7f;
            d[i] = s;
        }
        return d;
    }

    // ---------- Pièges, dégâts, mort ----------

    static float[] LaserOn(System.Random rnd)
    {
        var d = Buffer(0.32f);
        float phase = 0f;
        var bp = new BandPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float f = 300f * Mathf.Pow(7f, Mathf.Min(1f, t / 0.18f));
            phase += f / Rate;
            float s = Square(phase) * 0.4f + Sine(phase) * 0.4f + bp.Process(Noise(rnd), 4000f, 0.5f) * 0.4f;
            d[i] = s * AD(t, 0.005f, 0.09f);
        }
        return d;
    }

    // Bourdonnement électrique du faisceau (boucle d'une seconde).
    static float[] LaserHum(System.Random rnd)
    {
        var d = Buffer(1f);
        var bp = new BandPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float s = Saw(100f * t) * 0.35f + Sine(200f * t) * 0.3f + Square(300f * t) * 0.08f;
            s *= 0.8f + 0.2f * Mathf.Sin(TwoPi * 4f * t);
            float crackle = rnd.NextDouble() < 0.0015 ? Noise(rnd) * 3f : 0f;
            s += bp.Process(crackle, 4200f, 0.2f) * 0.5f;
            d[i] = s;
        }
        return d;
    }

    // Grésillement quand le faisceau traverse le personnage.
    static float[] LaserHit(System.Random rnd)
    {
        var d = Buffer(0.45f);
        var bp = new BandPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float ripple = 0.6f + 0.4f * Square(38f * t);
            float s = bp.Process(Noise(rnd), 3000f, 0.4f) * 0.8f + Square(60f * t) * 0.35f;
            d[i] = s * ripple * AD(t, 0.004f, 0.12f);
        }
        return d;
    }

    // Impact sourd des dégâts.
    static float[] Hurt(System.Random rnd)
    {
        var d = Buffer(0.4f);
        var lp = new LowPass();
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            phase += Mathf.Lerp(150f, 45f, Mathf.Min(1f, t / 0.2f)) / Rate;
            d[i] = Sine(phase) * AD(t, 0.003f, 0.12f) + lp.Process(Noise(rnd), 900f) * Decay(t, 0.015f) * 1.5f;
        }
        return d;
    }

    // Râle (voyelle qui glisse vers le grave) puis chute.
    static float[] Death(System.Random rnd)
    {
        var d = Buffer(1.5f);
        var f1 = new BandPass();
        var f2 = new BandPass();
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float k = Mathf.Min(1f, t / 1.1f);
            float pitch = 170f * Mathf.Pow(80f / 170f, k) * (1f + 0.03f * Mathf.Sin(TwoPi * 7f * t)) * (1f + Noise(rnd) * 0.01f);
            phase += pitch / Rate;
            float source = Saw(phase) + Noise(rnd) * 0.15f;
            float s = f1.Process(source, Mathf.Lerp(700f, 400f, k), 0.15f) + f2.Process(source, Mathf.Lerp(1150f, 800f, k), 0.2f) * 0.6f;
            s *= ASR(t, 0.05f, 0.3f, 1.15f);
            if (t > 1.1f)
            {
                float u = t - 1.1f;
                s += Sine((80f - 40f * u) * u) * Decay(u, 0.1f) * 1.2f;
            }
            d[i] = s;
        }
        return d;
    }

    // Petit carillon montant (réanimation).
    static float[] Revive()
    {
        var d = Buffer(1.6f);
        int[] notes = { 72, 76, 79, 84 };
        for (int n = 0; n < notes.Length; n++)
        {
            float f = Midi(notes[n]);
            int i0 = Mathf.RoundToInt(n * 0.12f * Rate);
            for (int i = i0; i < d.Length; i++)
            {
                float t = (float)(i - i0) / Rate;
                d[i] += (Sine(f * t) + Sine(f * 2.76f * t) * 0.35f + Sine(f * 5.4f * t) * 0.15f) * AD(t, 0.002f, 0.45f) * 0.4f;
            }
        }
        return d;
    }

    // ---------- Objets et Terminal ----------

    // Couinement de canard en plastique.
    static float[] Duck(System.Random rnd)
    {
        var d = Buffer(0.3f);
        var bp = new BandPass();
        var lp = new LowPass();
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float f = t < 0.12f ? Mathf.Lerp(950f, 1300f, t / 0.12f) : Mathf.Lerp(1300f, 1050f, (t - 0.12f) / 0.16f);
            phase += f / Rate;
            float pulse = Mathf.Repeat(phase, 1f) < 0.28f ? 1f : -0.4f;
            float s = bp.Process(pulse, 2400f, 0.5f) + lp.Process(pulse, 1800f) * 0.5f + Noise(rnd) * 0.05f;
            d[i] = s * ASR(t, 0.015f, 0.06f, 0.28f);
        }
        return d;
    }

    static float[] Pop()
    {
        var d = Buffer(0.16f);
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            phase += Mathf.Lerp(650f, 200f, Mathf.Min(1f, t / 0.08f)) / Rate;
            d[i] = Sine(phase) * AD(t, 0.002f, 0.04f);
        }
        return d;
    }

    // Croquette croquée : trois craquements.
    static float[] Eat(System.Random rnd)
    {
        var d = Buffer(0.5f);
        var lp = new LowPass();
        var bp = new BandPass();
        foreach (float start in new[] { 0f, 0.15f, 0.31f })
        {
            int i0 = Mathf.RoundToInt(start * Rate);
            for (int i = i0; i < d.Length; i++)
            {
                float t = (float)(i - i0) / Rate;
                float grain = rnd.NextDouble() < 0.08 ? Noise(rnd) * 2f : Noise(rnd) * 0.3f;
                d[i] += (lp.Process(grain, 1800f) + bp.Process(grain, 900f, 0.5f) * 0.5f) * AD(t, 0.003f, 0.04f);
            }
        }
        return d;
    }

    static float[] Beep(float freq, float length)
    {
        var d = Buffer(length);
        var lp = new LowPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            d[i] = lp.Process(Square(freq * t), 3000f) * ASR(t, 0.005f, 0.03f, length);
        }
        return d;
    }

    static float[] ErrorBeep()
    {
        var d = Buffer(0.34f);
        var lp = new LowPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float env = ASR(t, 0.005f, 0.02f, 0.12f) + ASR(t - 0.18f, 0.005f, 0.02f, 0.14f);
            d[i] = lp.Process(Square(196f * t) + Square(207f * t) * 0.6f, 2000f) * env;
        }
        return d;
    }

    // Caisse enregistreuse : mécanisme puis « ding ».
    static float[] Register(System.Random rnd)
    {
        var d = Buffer(1f);
        var bp = new BandPass();
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float impulse = 0f;
            if (t < 0.24f)
            {
                phase += 28f / Rate;
                if (phase >= 1f) { phase -= 1f; impulse = 1f + Noise(rnd) * 0.3f; }
            }
            float s = bp.Process(impulse, 1500f, 0.08f) * 0.8f;
            if (t > 0.24f)
            {
                float u = t - 0.24f;
                s += (Sine(1568f * u) + Sine(2093f * u) * 0.6f + Sine(4320f * u) * 0.2f) * AD(u, 0.002f, 0.3f) * 0.6f;
            }
            d[i] = s;
        }
        return d;
    }

    // ---------- Ambiances ----------

    // Valse de cirque étouffée (orgue de barbarie), 8 mesures à 3 temps en boucle.
    static float[] HubMusic(System.Random rnd)
    {
        const float beat = 60f / 168f;
        float loop = beat * 24f;
        var d = Buffer(loop);
        int[][] melody =
        {
            new[] { 72, 76, 79 }, new[] { 81, 81, 79 }, new[] { 77, 81, 77 }, new[] { 76, 76, 72 },
            new[] { 74, 77, 81 }, new[] { 79, 77, 74 }, new[] { 76, 79, 71 }, new[] { 72, 72, 72 },
        };
        int[] roots = { 48, 53, 53, 48, 50, 55, 55, 48 };
        int[][] chords = { new[] { 64, 67 }, new[] { 65, 69 }, new[] { 65, 69 }, new[] { 64, 67 }, new[] { 65, 69 }, new[] { 62, 65 }, new[] { 62, 65 }, new[] { 64, 67 } };

        void Note(float f, float start, float length, float volume, bool pipe)
        {
            int i0 = Mathf.RoundToInt(start * Rate), n = Mathf.RoundToInt(length * Rate);
            for (int i = 0; i < n; i++)
            {
                int idx = (i0 + i) % d.Length;
                float t = (float)i / Rate, abs = (float)(i0 + i) / Rate;
                // Pleurage de bande (période multiple de la boucle).
                float wow = 1f + 0.006f * Mathf.Sin(TwoPi * 3f / loop * abs);
                float s = pipe ? Sine(f * wow * t) + Sine(2f * f * wow * t) * 0.5f + Sine(3f * f * 1.004f * t) * 0.25f
                               : Square(f * wow * t) * 0.5f + Sine(f * wow * t);
                d[idx] += s * ASR(t, 0.012f, 0.05f, length) * volume;
            }
        }

        for (int bar = 0; bar < 8; bar++)
        {
            float b0 = bar * 3f * beat;
            for (int k = 0; k < 3; k++)
            {
                bool held = bar == 7;
                if (held && k > 0) continue;
                Note(Midi(melody[bar][k]) * (1f + Noise(rnd) * 0.004f), b0 + k * beat, held ? beat * 2.9f : beat * 0.9f, 0.35f, true);
            }
            Note(Midi(roots[bar] - 12), b0, beat * 0.8f, 0.5f, false);
            for (int k = 1; k < 3; k++)
                foreach (int c in chords[bar]) Note(Midi(c - 12), b0 + k * beat, beat * 0.45f, 0.15f, false);
        }

        // Étouffé (joué derrière une toile) avec un léger écho.
        var lp1 = new LowPass();
        var lp2 = new LowPass();
        var dry = (float[])d.Clone();
        int delay = Mathf.RoundToInt(0.11f * Rate);
        for (int i = 0; i < d.Length; i++)
        {
            float s = dry[i] + dry[(i - delay + d.Length) % d.Length] * 0.3f + Noise(rnd) * 0.01f;
            d[i] = lp2.Process(lp1.Process(s, 1500f), 1800f);
        }
        return d;
    }

    // Nappe grave du labyrinthe : basses qui battent, souffle, grincements métalliques lointains.
    static float[] MazeDrone(System.Random rnd)
    {
        const float loop = 12f;
        var d = Buffer(loop + 1.5f);
        var wind = new BandPass();
        var metal = new BandPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float s = Sine(41f * t) * 0.35f + Sine(43.5f * t) * 0.3f + Sine(61.5f * t) * 0.12f * (0.5f + 0.5f * Sine(t / 6f));
            float gust = 0.5f + 0.5f * Sine(t / 4f + 0.2f);
            s += wind.Process(Noise(rnd), 350f + 250f * Sine(t / 12f), 0.3f) * 0.18f * gust;
            float impulse = 0f;
            foreach (float at in new[] { 3.7f, 8.9f })
                if (t > at && t < at + 0.6f && rnd.NextDouble() < 0.02) impulse = Noise(rnd);
            s += metal.Process(impulse, 620f, 0.02f) * 0.5f;
            d[i] = s;
        }
        return MakeLoop(d, loop, 1.2f);
    }

    // Grouillement d'insectes (boucle) : petits cliquetis de pattes et frottements de carapaces.
    static float[] Insects(System.Random rnd)
    {
        const float loop = 4f;
        var d = Buffer(loop + 0.6f);
        var bp = new BandPass();
        var hiss = new BandPass();
        float freq = 4000f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float density = 0.004f + 0.006f * (0.5f + 0.5f * Sine(t / 2f));
            float impulse = 0f;
            if (rnd.NextDouble() < density)
            {
                impulse = Noise(rnd) * (0.5f + (float)rnd.NextDouble());
                freq = 3000f + (float)rnd.NextDouble() * 3500f;
            }
            d[i] = bp.Process(impulse, freq, 0.25f) + hiss.Process(Noise(rnd), 6000f, 0.8f) * 0.06f;
        }
        return MakeLoop(d, loop, 0.5f);
    }

    // Néon qui grésille en s'éteignant.
    static float[] Neon(System.Random rnd)
    {
        var d = Buffer(0.25f);
        var bp = new BandPass();
        float gate = 1f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            if (i % (Rate / 50) == 0) gate = rnd.NextDouble() < 0.65 ? 1f : 0.1f;
            float s = Square(120f * t) * 0.4f + Saw(240f * t) * 0.2f + bp.Process(Noise(rnd), 3500f, 0.4f) * 0.6f;
            d[i] = s * gate * ASR(t, 0.005f, 0.05f, 0.25f);
        }
        return d;
    }

    // Clignement humide d'un œil.
    static float[] Blink(System.Random rnd)
    {
        var d = Buffer(0.2f);
        var lp = new LowPass();
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            phase += (Mathf.Lerp(240f, 110f, t / 0.2f) * (1f + 0.15f * Mathf.Sin(TwoPi * 45f * t))) / Rate;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.18f));
            d[i] = (Sine(phase) * 0.6f + lp.Process(Noise(rnd), 600f) * 1.2f) * env * env;
        }
        return d;
    }

    // ---------- Pas ----------

    static float[] StepTile(System.Random rnd)
    {
        var d = Buffer(0.09f);
        var bp = new BandPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            d[i] = bp.Process(Noise(rnd), 2400f, 0.4f) * Decay(t, 0.007f) + Sine(170f * t) * Decay(t, 0.02f) * 0.7f;
        }
        return d;
    }

    static float[] StepDirt(System.Random rnd)
    {
        var d = Buffer(0.13f);
        var lp = new LowPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float grain = rnd.NextDouble() < 0.1 ? Noise(rnd) * 2f : Noise(rnd) * 0.4f;
            d[i] = lp.Process(grain, 1300f) * AD(t, 0.005f, 0.035f);
        }
        return d;
    }

    // ---------- Abjuration ----------

    // Glas d'une cloche funèbre (moins de 2 secondes) : partiels inharmoniques d'une grosse cloche.
    static float[] Bell(System.Random rnd)
    {
        var d = Buffer(1.95f);
        float f = 98f;
        (float ratio, float amp, float decay)[] partials =
        {
            (0.5f, 0.5f, 1.6f), (1f, 0.8f, 1.2f), (1.19f, 0.6f, 0.9f), (1.5f, 0.35f, 0.7f),
            (2f, 0.45f, 0.6f), (2.52f, 0.25f, 0.4f), (3.01f, 0.18f, 0.3f), (4.1f, 0.1f, 0.2f),
        };
        var lp = new LowPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float s = 0f;
            foreach (var (ratio, amp, decay) in partials)
                s += Sine(f * ratio * t * (1f + 0.0015f * Mathf.Sin(TwoPi * 2.5f * t))) * amp * Decay(t, decay);
            // Choc du battant.
            s += lp.Process(Noise(rnd), 2500f) * Decay(t, 0.012f) * 1.5f;
            d[i] = s * Mathf.Clamp01((1.95f - t) / 0.3f);
        }
        return d;
    }

    // Petite musique horrifique au tempo lent : bourdon dissonant, boîte à musique désaccordée, souffles.
    static float[] AbjurationMusic(System.Random rnd)
    {
        const float beat = 60f / 48f;
        float loop = beat * 16f;
        var d = Buffer(loop + 2f);
        int[] melody = { 74, 77, 76, 73, 74, 81, 80, 77, 74, 70, 69, 73, 74, 77, 73, 62 };
        for (int n = 0; n < melody.Length; n++)
        {
            float f = Midi(melody[n]) * (1f + Noise(rnd) * 0.006f);
            int i0 = Mathf.RoundToInt(n * beat * Rate);
            for (int i = i0; i < d.Length; i++)
            {
                float t = (float)(i - i0) / Rate;
                if (t > 3f) break;
                // Lame de boîte à musique : fondamentale, partiel aigu, légère déformation du ressort.
                float wow = 1f + 0.004f * Mathf.Sin(TwoPi * 0.8f * t);
                d[i] += (Sine(f * wow * t) + Sine(f * 2.99f * t) * 0.25f + Sine(f * 5.4f * t) * 0.08f) * AD(t, 0.004f, 0.9f) * 0.3f;
            }
        }
        var wind = new BandPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float swell = 0.5f + 0.5f * Mathf.Sin(TwoPi * t / loop * 2f);
            float drone = Sine(73.4f * t) * 0.35f + Sine(77.8f * t) * 0.25f + Sine(110f * t) * 0.1f * swell;
            drone += wind.Process(Noise(rnd), 500f + 300f * swell, 0.2f) * 0.12f * swell;
            d[i] += drone;
        }
        return MakeLoop(d, loop, 1.5f);
    }

    // ---------- Post-It, orbes, porte ----------

    // Papier qu'on décolle ou qu'on colle.
    static float[] PostIt(System.Random rnd)
    {
        var d = Buffer(0.18f);
        var lp = new LowPass();
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float x = Noise(rnd) * (rnd.NextDouble() < 0.3 ? 1.5f : 0.5f);
            d[i] = (x - lp.Process(x, 1800f)) * AD(t, 0.01f, 0.05f);
        }
        return d;
    }

    // Orbe qui s'éveille : souffle qui monte et carillon de verre.
    static float[] OrbOn(System.Random rnd)
    {
        var d = Buffer(1.8f);
        var bp = new BandPass();
        int[] notes = { 74, 81, 86, 93 };
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float s = bp.Process(Noise(rnd), 300f + 1800f * Mathf.Min(1f, t / 1.2f), 0.3f) * ASR(t, 0.6f, 0.6f, 1.8f) * 0.5f;
            for (int n = 0; n < notes.Length; n++)
            {
                float u = t - 0.5f - n * 0.12f;
                if (u > 0f) s += Sine(Midi(notes[n]) * u) * AD(u, 0.003f, 0.6f) * 0.25f;
            }
            d[i] = s;
        }
        return d;
    }

    // Orbe qui se brise : éclats de verre et soupir qui s'échappe.
    static float[] OrbBreak(System.Random rnd)
    {
        var d = Buffer(1.4f);
        var hp = new LowPass();
        var pings = new (float t, float f)[10];
        for (int k = 0; k < pings.Length; k++) pings[k] = ((float)rnd.NextDouble() * 0.5f, 2500f + (float)rnd.NextDouble() * 4500f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float x = Noise(rnd);
            float s = (x - hp.Process(x, 3000f)) * Decay(t, 0.08f) * 1.2f;
            foreach (var (pt, pf) in pings)
                if (t > pt) s += Sine(pf * (t - pt)) * Decay(t - pt, 0.15f) * 0.2f;
            s += Sine(Mathf.Lerp(330f, 110f, t / 1.4f) * t) * ASR(t - 0.2f, 0.2f, 0.6f, 1.1f) * 0.25f;
            d[i] = s;
        }
        return d;
    }

    // Porte de la Zone 2 qui s'ouvre : grondement de pierre et chœur fantomatique.
    static float[] ZoneDoor(System.Random rnd)
    {
        var d = Buffer(4.2f);
        var rumble = new LowPass();
        var choir1 = new BandPass();
        var choir2 = new BandPass();
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float s = rumble.Process(Noise(rnd), 120f) * 3f * ASR(t, 0.3f, 1.2f, 4f);
            phase += (110f + 20f * Mathf.Min(1f, t / 3f)) / Rate;
            float voice = Saw(phase) + Saw(phase * 1.5f) * 0.6f + Saw(phase * 2.01f) * 0.4f;
            s += (choir1.Process(voice, 700f, 0.15f) + choir2.Process(voice, 1100f, 0.2f) * 0.6f) * ASR(t, 1f, 1.5f, 4.2f) * 0.4f;
            d[i] = s;
        }
        return d;
    }

    // ---------- La Dame ----------

    // Cri strident quand elle prend un joueur en chasse.
    static float[] DameScream(System.Random rnd)
    {
        var d = Buffer(1.6f);
        var f1 = new BandPass();
        var f2 = new BandPass();
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float pitch = Mathf.Lerp(420f, 780f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t / 0.5f))) * (1f + 0.04f * Mathf.Sin(TwoPi * 9f * t));
            phase += pitch / Rate;
            float src = Saw(phase) + Noise(rnd) * 0.35f;
            float s = f1.Process(src, 1100f, 0.12f) + f2.Process(src, 2900f, 0.15f) * 0.7f;
            d[i] = s * ASR(t, 0.03f, 0.6f, 1.6f);
        }
        return d;
    }

    // Attaque : souffle et râle aigu.
    static float[] DameAttack(System.Random rnd)
    {
        var d = Buffer(1f);
        var bp = new BandPass();
        var f1 = new BandPass();
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float s = bp.Process(Noise(rnd), Mathf.Lerp(400f, 2200f, t), 0.4f) * ASR(t, 0.15f, 0.4f, 0.9f) * 0.8f;
            phase += Mathf.Lerp(600f, 300f, t) / Rate;
            s += f1.Process(Saw(phase) + Noise(rnd) * 0.4f, 1500f, 0.15f) * ASR(t - 0.35f, 0.02f, 0.35f, 0.6f);
            d[i] = s;
        }
        return d;
    }
}
