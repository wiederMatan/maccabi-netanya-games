using UnityEngine;

namespace MathStrikers
{
    /// <summary>
    /// All match audio, synthesised at startup rather than shipped as files. A few
    /// short procedural clips cost nothing to download and keep the WebGL build the
    /// same size, which matters on a portal children open on phones.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class MatchAudio : MonoBehaviour
    {
        const int SampleRate = 44100;

        AudioSource source;
        AudioClip kick;
        AudioClip goal;
        AudioClip miss;
        AudioClip whistle;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            kick = BuildKick();
            goal = BuildGoal();
            miss = BuildMiss();
            whistle = BuildWhistle();
        }

        public void PlayKick() => Play(kick, 0.85f);
        public void PlayGoal() => Play(goal, 0.7f);
        public void PlayMiss() => Play(miss, 0.6f);
        public void PlayWhistle() => Play(whistle, 0.6f);

        void Play(AudioClip clip, float volume)
        {
            if (clip != null && source != null) source.PlayOneShot(clip, volume);
        }

        // ---------------------------------------------------------------- synthesis

        static AudioClip Create(string name, float seconds, System.Func<float, float, float> sample)
        {
            int count = Mathf.CeilToInt(SampleRate * seconds);
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = Mathf.Clamp(sample(t, t / seconds), -1f, 1f);
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A boot on a ball: a click of noise over a short low thump.</summary>
        static AudioClip BuildKick()
        {
            var random = new System.Random(7);
            return Create("Kick", 0.18f, (t, phase) =>
            {
                float envelope = Mathf.Exp(-22f * t);
                float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(150f, 60f, phase) * t);
                float crack = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-70f * t);
                return (thump * 0.75f + crack * 0.5f) * envelope;
            });
        }

        /// <summary>A rising three-note flourish - the "you got it" sound.</summary>
        static AudioClip BuildGoal()
        {
            float[] notes = { 523.25f, 659.25f, 783.99f };   // C5, E5, G5
            const float step = 0.11f;

            return Create("Goal", 0.55f, (t, phase) =>
            {
                int index = Mathf.Min(notes.Length - 1, (int)(t / step));
                float local = t - index * step;
                float envelope = Mathf.Exp(-7f * local) * (1f - phase * 0.25f);
                float tone = Mathf.Sin(2f * Mathf.PI * notes[index] * t)
                           + 0.35f * Mathf.Sin(4f * Mathf.PI * notes[index] * t);
                return tone * envelope * 0.5f;
            });
        }

        /// <summary>A short falling tone, clearly not a celebration.</summary>
        static AudioClip BuildMiss()
        {
            return Create("Miss", 0.4f, (t, phase) =>
            {
                float frequency = Mathf.Lerp(370f, 180f, phase);
                float envelope = Mathf.Exp(-6f * t);
                return Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.55f;
            });
        }

        /// <summary>Referee's whistle, for the end of a match.</summary>
        static AudioClip BuildWhistle()
        {
            var random = new System.Random(19);
            return Create("Whistle", 0.75f, (t, phase) =>
            {
                float envelope = Mathf.Min(1f, t * 22f) * Mathf.Exp(-3.2f * t);
                // The warble is what makes it read as a whistle rather than a beep.
                float warble = Mathf.Sin(2f * Mathf.PI * 18f * t) * 55f;
                float tone = Mathf.Sin(2f * Mathf.PI * (2300f + warble) * t);
                float air = (float)(random.NextDouble() * 2.0 - 1.0) * 0.12f;
                return (tone + air) * envelope * 0.5f;
            });
        }
    }
}
