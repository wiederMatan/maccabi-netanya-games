using System.Collections;
using UnityEngine;

namespace Juggling
{
    /// <summary>
    /// All game audio. Stadium sounds - crowd, whistle, cheer, groan, applause - are
    /// real recordings loaded from Resources/Audio (CC0 / public domain, credited in
    /// the README). The touch "boop" is synthesised, and every recording falls back
    /// to a synthesised stand-in if its clip is missing, so the game is never silent.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class JuggleAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const float CrowdVolume = 0.3f;
        const float CrowdSwellVolume = 0.75f;

        AudioSource source;
        AudioSource crowdSource;
        Coroutine crowdRamp;

        AudioClip touch;
        AudioClip cheer;
        AudioClip drop;
        AudioClip whistle;
        AudioClip applause;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            touch = BuildTouch();
            cheer = Load("Cheer") ?? BuildCheer();
            drop = Load("Ohh") ?? BuildDrop();
            whistle = Load("Whistle") ?? BuildWhistle();
            applause = Load("Applause");

            var crowd = Load("Crowd");
            if (crowd != null)
            {
                crowdSource = gameObject.AddComponent<AudioSource>();
                crowdSource.clip = crowd;
                crowdSource.loop = true;
                crowdSource.playOnAwake = false;
                crowdSource.spatialBlend = 0f;
                crowdSource.volume = 0f;
            }
        }

        static AudioClip Load(string name) => Resources.Load<AudioClip>("Audio/" + name);

        /// <summary>
        /// One touch of the ball. The note climbs with the combo, so a good run
        /// sounds like it is building.
        /// </summary>
        public void PlayTouch(int combo, bool clean)
        {
            float pitch = 1f + Mathf.Min(combo, 12) * 0.04f + (clean ? 0.06f : 0f);
            Play(touch, 0.8f, pitch);
        }

        /// <summary>Kick-off: one blast of the whistle, and the crowd settles in.</summary>
        public void PlayWhistle()
        {
            Play(whistle, 0.55f);
            StartCrowd();
        }

        /// <summary>A milestone: a cheer, with the whole crowd lifting behind it.</summary>
        public void PlayCheer()
        {
            Play(cheer, 0.8f);
            SwellCrowd();
        }

        public void PlayDrop() => Play(drop, 0.55f);

        /// <summary>End of a round: applause if it was a new best.</summary>
        public void PlayRoundOver(bool applaud)
        {
            if (!applaud) return;
            if (applause != null) Play(applause, 0.8f);
            SwellCrowd();
        }

        /// <summary>
        /// Called from the web page through SendMessage("JuggleAudio", "SetMuted", "1"/"0"),
        /// so the page's sound button silences the game too.
        /// </summary>
        public void SetMuted(string value)
        {
            AudioListener.volume = value == "1" ? 0f : 1f;
        }

        void StartCrowd()
        {
            if (crowdSource == null) return;
            if (!crowdSource.isPlaying) crowdSource.Play();
            RampCrowd(CrowdVolume, 1.5f);
        }

        void SwellCrowd()
        {
            if (crowdSource == null || !crowdSource.isPlaying) return;
            if (crowdRamp != null) StopCoroutine(crowdRamp);
            crowdRamp = StartCoroutine(Swell());
        }

        IEnumerator Swell()
        {
            yield return Ramp(CrowdSwellVolume, 0.4f);
            yield return new WaitForSeconds(1.2f);
            yield return Ramp(CrowdVolume, 1.8f);
            crowdRamp = null;
        }

        void RampCrowd(float target, float seconds)
        {
            if (crowdRamp != null) StopCoroutine(crowdRamp);
            crowdRamp = StartCoroutine(Ramp(target, seconds));
        }

        IEnumerator Ramp(float target, float seconds)
        {
            float from = crowdSource.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                crowdSource.volume = Mathf.Lerp(from, target, t / seconds);
                yield return null;
            }
            crowdSource.volume = target;
        }

        void Play(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null || source == null) return;
            if (Mathf.Approximately(pitch, 1f))
            {
                source.PlayOneShot(clip, volume);
                return;
            }
            // PlayOneShot shares the source's pitch, so pitched shots get their own source.
            var shot = gameObject.AddComponent<AudioSource>();
            shot.spatialBlend = 0f;
            shot.pitch = pitch;
            shot.PlayOneShot(clip, volume);
            Destroy(shot, clip.length / pitch + 0.1f);
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

        /// <summary>
        /// A light "boop": a soft thump for the foot, under a round tone that
        /// bends upward - friendlier than a full kick for a sound heard every second.
        /// </summary>
        static AudioClip BuildTouch()
        {
            var random = new System.Random(11);
            return Create("Touch", 0.2f, (t, phase) =>
            {
                float envelope = Mathf.Min(1f, t * 400f) * Mathf.Exp(-16f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(420f, 640f, phase) * t);
                float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(140f, 70f, phase) * t) * Mathf.Exp(-30f * t);
                float tap = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-120f * t);
                return (tone * 0.45f + thump * 0.6f + tap * 0.25f) * envelope;
            });
        }

        /// <summary>A rising three-note flourish, used if the cheer recording is missing.</summary>
        static AudioClip BuildCheer()
        {
            float[] notes = { 523.25f, 659.25f, 783.99f };   // C5, E5, G5
            const float step = 0.11f;

            return Create("Cheer", 0.55f, (t, phase) =>
            {
                int index = Mathf.Min(notes.Length - 1, (int)(t / step));
                float local = t - index * step;
                float envelope = Mathf.Exp(-7f * local) * (1f - phase * 0.25f);
                float tone = Mathf.Sin(2f * Mathf.PI * notes[index] * t)
                           + 0.35f * Mathf.Sin(4f * Mathf.PI * notes[index] * t);
                return tone * envelope * 0.5f;
            });
        }

        /// <summary>A short falling tone, used if the groan recording is missing.</summary>
        static AudioClip BuildDrop()
        {
            return Create("Drop", 0.4f, (t, phase) =>
            {
                float frequency = Mathf.Lerp(370f, 180f, phase);
                float envelope = Mathf.Exp(-6f * t);
                return Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.55f;
            });
        }

        /// <summary>Synthesised referee's whistle, used if the recording is missing.</summary>
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
