using System.Collections;
using UnityEngine;

namespace MathStrikers
{
    /// <summary>
    /// All match audio. Stadium sounds - crowd, whistle, cheer, groan, applause - are
    /// real recordings loaded from Resources/Audio (CC0 / public domain, credited in
    /// the README). The kick is synthesised, and every recording falls back to a
    /// synthesised stand-in if its clip is missing, so the match is never silent.
    ///
    /// The music is our own (CC0, Resources/Audio/Music): a stadium anthem loops
    /// behind the start and full-time cards, supporters' drums loop during play,
    /// and short stings mark a goal, the result and each star earned. Everything
    /// plays through the AudioListener, so the page's mute silences it all.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class MatchAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const float CrowdVolume = 0.35f;
        const float CrowdSwellVolume = 0.75f;

        const float AnthemVolume = 0.3f;
        const float DrumsVolume = 0.22f;
        const float UrgentDrumsPitch = 1.08f;

        public const string Anthem = "MusicAnthem";
        public const string Drums = "MusicDrums";
        public const string GoalSting = "MusicGoal";
        public const string Win = "MusicWin";
        public const string Star = "MusicStar";
        public const string TryAgain = "MusicTryAgain";

        /// <summary>Every music clip, and whether it loops (VerifyScene checks these load).</summary>
        public static readonly (string name, bool loops)[] MusicClips =
        {
            (Anthem, true), (Drums, true), (GoalSting, false), (Win, false), (Star, false), (TryAgain, false)
        };

        public static MatchAudio Instance { get; private set; }

        public AudioSource AnthemSource => anthemSource;
        public AudioSource DrumsSource => drumsSource;

        AudioSource source;
        AudioSource crowdSource;
        Coroutine crowdRamp;

        AudioClip kick;
        AudioClip goal;
        AudioClip miss;
        AudioClip whistle;
        AudioClip applause;
        AudioClip tick;

        AudioSource anthemSource;
        AudioSource drumsSource;
        Coroutine anthemFade;
        Coroutine drumsFade;
        Coroutine anthemReturn;
        AudioClip goalSting;
        AudioClip winSting;
        AudioClip starSting;
        AudioClip tryAgainSting;

        void Awake()
        {
            Instance = this;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            kick = BuildKick();
            tick = BuildTick();
            goal = Load("Cheer") ?? BuildGoal();
            miss = Load("Ohh") ?? BuildMiss();
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

            anthemSource = MusicSource(LoadMusic(Anthem));
            drumsSource = MusicSource(LoadMusic(Drums));
            goalSting = LoadMusic(GoalSting);
            winSting = LoadMusic(Win);
            starSting = LoadMusic(Star);
            tryAgainSting = LoadMusic(TryAgain);
        }

        public static AudioClip LoadMusic(string name) => Resources.Load<AudioClip>("Audio/Music/" + name);

        AudioSource MusicSource(AudioClip clip)
        {
            if (clip == null) return null;
            var music = gameObject.AddComponent<AudioSource>();
            music.clip = clip;
            music.loop = true;
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            music.volume = 0f;
            return music;
        }

        // ---------------------------------------------------------------- music

        /// <summary>The anthem behind the start and full-time cards.</summary>
        public void PlayMenuMusic()
        {
            if (anthemReturn != null) { StopCoroutine(anthemReturn); anthemReturn = null; }
            FadeIn(anthemSource, AnthemVolume, 1f, ref anthemFade);
        }

        /// <summary>Kick-off: the anthem gives way to the supporters' drums.</summary>
        public void PlayMatchMusic()
        {
            if (anthemReturn != null) { StopCoroutine(anthemReturn); anthemReturn = null; }
            FadeOut(anthemSource, 0.8f, ref anthemFade);
            SetUrgent(false);
            FadeIn(drumsSource, DrumsVolume, 1.2f, ref drumsFade);
        }

        /// <summary>The drums push on a little while the answer clock runs low.</summary>
        public void SetUrgent(bool urgent)
        {
            if (drumsSource != null) drumsSource.pitch = urgent ? UrgentDrumsPitch : 1f;
        }

        /// <summary>One sparkle per star as it fills on the full-time card.</summary>
        public void PlayStar() => Play(starSting, 0.7f);

        void FadeIn(AudioSource music, float volume, float seconds, ref Coroutine fade)
        {
            if (music == null) return;
            if (fade != null) StopCoroutine(fade);
            if (!music.isPlaying) { music.volume = 0f; music.Play(); }
            fade = StartCoroutine(Fade(music, volume, seconds, false));
        }

        void FadeOut(AudioSource music, float seconds, ref Coroutine fade)
        {
            if (music == null || !music.isPlaying) return;
            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(Fade(music, 0f, seconds, true));
        }

        static IEnumerator Fade(AudioSource music, float target, float seconds, bool stopAtEnd)
        {
            float from = music.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                music.volume = Mathf.Lerp(from, target, t / seconds);
                yield return null;
            }
            music.volume = target;
            if (stopAtEnd) music.Stop();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static AudioClip Load(string name) => Resources.Load<AudioClip>("Audio/" + name);

        /// <summary>The button press tick. Goes through the listener, so mute silences it.</summary>
        public void PlayTick() => Play(tick, 0.35f);

        public void PlayKick() => Play(kick, 0.85f);
        public void PlayMiss() => Play(miss, 0.5f);

        /// <summary>Kick-off: one blast of the whistle, and the crowd settles in.</summary>
        public void PlayWhistle()
        {
            Play(whistle, 0.6f);
            StartCrowd();
        }

        /// <summary>A goal: the goal sting over a softer cheer, with the crowd lifting behind it.</summary>
        public void PlayGoal()
        {
            Play(goal, 0.5f);
            Play(goalSting, 0.75f);
            SwellCrowd();
        }

        /// <summary>
        /// Full time: the drums stop, two short blasts and a long one, then the win
        /// fanfare (or the "nearly!" sting), applause for a win or draw, and the
        /// anthem back once the sting has finished.
        /// </summary>
        public void PlayFullTime(bool won, bool drew)
        {
            FadeOut(drumsSource, 0.6f, ref drumsFade);
            StartCoroutine(FullTime(won, drew));
        }

        IEnumerator FullTime(bool won, bool drew)
        {
            bool applaud = won || drew;
            Play(whistle, 0.55f, 1.05f);
            yield return new WaitForSeconds(0.35f);
            Play(whistle, 0.55f, 1.05f);
            yield return new WaitForSeconds(0.35f);
            Play(whistle, 0.75f, 0.97f);
            yield return new WaitForSeconds(0.25f);
            if (applaud && applause != null) Play(applause, 0.8f);
            if (applaud) SwellCrowd();

            var sting = won ? winSting : tryAgainSting;
            Play(sting, 0.8f);
            anthemReturn = StartCoroutine(AnthemAfter(sting != null ? sting.length : 0.5f));
        }

        IEnumerator AnthemAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            anthemReturn = null;
            FadeIn(anthemSource, AnthemVolume, 1.5f, ref anthemFade);
        }

        /// <summary>
        /// Called from the web page through SendMessage("MatchAudio", "SetMuted", "1"/"0"),
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

        /// <summary>A short, soft wooden tick for button presses.</summary>
        static AudioClip BuildTick()
        {
            return Create("Tick", 0.05f, (t, phase) =>
            {
                float envelope = Mathf.Exp(-90f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * 1250f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 2600f * t);
                return tone * envelope * 0.6f;
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
