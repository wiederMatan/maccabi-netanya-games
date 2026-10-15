using System.Collections;
using UnityEngine;

namespace Headers
{
    /// <summary>
    /// All game audio. Stadium sounds - crowd, whistle, cheer, groan, applause - are
    /// real recordings loaded from Resources/Audio (CC0 / public domain, credited in
    /// the README). The chime, the kick, the header and the jump swoosh are
    /// synthesised, and every recording falls back to a synthesised stand-in if its
    /// clip is missing, so the game is never silent.
    ///
    /// Music is the club's own (CC0, Resources/Audio/Music): a stadium anthem loops
    /// behind the end card, supporters' drums loop under the crowd during
    /// a round and quicken as it gets harder, the goal sting plays for each goal,
    /// and a result sting marks the end of a round. Everything plays through the AudioListener, so SetMuted
    /// silences the music too.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class HeaderAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const float CrowdVolume = 0.32f;
        const float CrowdSwellVolume = 0.7f;

        AudioSource source;
        AudioSource crowdSource;
        Coroutine crowdRamp;

        const float AnthemVolume = 0.3f;
        const float DrumsVolume = 0.22f;
        const float MusicFadeSeconds = 0.8f;
        const float DrumsTopPitch = 1.12f;

        /// <summary>The music clips and whether each one loops. Checked by VerifyScene.</summary>
        public static readonly (string clip, bool loop)[] Music =
        {
            ("MusicAnthem", true), ("MusicDrums", true), ("MusicGoal", false),
            ("MusicWin", false), ("MusicStar", false), ("MusicTryAgain", false)
        };

        AudioSource anthemSource;
        AudioSource drumsSource;
        Coroutine anthemFade;
        Coroutine drumsFade;
        AudioClip goalSting;
        AudioClip winSting;
        AudioClip tryAgainSting;

        AudioClip chime;
        AudioClip tick;
        AudioClip knock;
        AudioClip swoosh;
        AudioClip touch;
        AudioClip thump;
        AudioClip cheer;
        AudioClip ohh;
        AudioClip whistle;
        AudioClip applause;

        /// <summary>The scene's audio, for the button press feedback.</summary>
        public static HeaderAudio Instance { get; private set; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Awake()
        {
            Instance = this;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            chime = BuildChime();
            tick = BuildTick();
            knock = BuildKnock();
            swoosh = BuildSwoosh();
            touch = BuildTouch();
            thump = BuildThump();
            cheer = Load("Cheer") ?? BuildFanfare();
            ohh = Load("Ohh") ?? BuildGroan();
            whistle = Load("Whistle") ?? BuildWhistle();
            applause = Load("Applause");

            anthemSource = LoopSource(Load("Music/MusicAnthem"));
            drumsSource = LoopSource(Load("Music/MusicDrums"));
            goalSting = Load("Music/MusicGoal");
            winSting = Load("Music/MusicWin");
            tryAgainSting = Load("Music/MusicTryAgain");

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

        public static AudioClip Load(string name) => Resources.Load<AudioClip>("Audio/" + name);

        AudioSource LoopSource(AudioClip clip)
        {
            if (clip == null) return null;
            var loop = gameObject.AddComponent<AudioSource>();
            loop.clip = clip;
            loop.loop = true;
            loop.playOnAwake = false;
            loop.spatialBlend = 0f;
            loop.volume = 0f;
            return loop;
        }

        /// <summary>The anthem behind the start and end cards.</summary>
        public void PlayMenuMusic()
        {
            FadeMusic(anthemSource, ref anthemFade, AnthemVolume, MusicFadeSeconds);
            FadeMusic(drumsSource, ref drumsFade, 0f, 0.3f);
        }

        /// <summary>The next round is being counted in: the anthem bows out.</summary>
        public void FadeOutMenuMusic() => FadeMusic(anthemSource, ref anthemFade, 0f, MusicFadeSeconds);

        /// <summary>A countdown beep: a short chime for 3, 2, 1, and a higher one for "go".</summary>
        public void PlayCount(bool go) => Play(chime, go ? 0.55f : 0.35f, go ? 1.5f : 0.75f);

        /// <summary>Kick-off: the anthem gives way to the supporters' drums.</summary>
        public void PlayRoundMusic()
        {
            FadeMusic(anthemSource, ref anthemFade, 0f, MusicFadeSeconds);
            if (drumsSource != null)
            {
                drumsSource.pitch = 1f;
                drumsSource.time = 0f;
            }
            FadeMusic(drumsSource, ref drumsFade, DrumsVolume, 0.4f);
        }

        /// <summary>
        /// The drums quicken through the round: 0 at the first corner, 1 at the
        /// last. The rise is gentle, so the beat still sits under the crowd.
        /// </summary>
        public void SetTempo(float progress)
        {
            if (drumsSource != null) drumsSource.pitch = Mathf.Lerp(1f, DrumsTopPitch, Mathf.Clamp01(progress));
        }

        /// <summary>The round is over: the drums stop.</summary>
        public void StopRoundMusic() => FadeMusic(drumsSource, ref drumsFade, 0f, 0.3f);

        /// <summary>
        /// As the end card appears: a fanfare for three stars, the goal sting for two,
        /// a "nearly!" for one, and the anthem comes back in underneath.
        /// </summary>
        public void PlayResult(int stars)
        {
            var sting = stars >= 3 ? winSting : stars == 2 ? goalSting : tryAgainSting;
            Play(sting, 0.75f);
            PlayMenuMusic();
        }

        void FadeMusic(AudioSource music, ref Coroutine fade, float target, float seconds)
        {
            if (music == null) return;
            if (fade != null) StopCoroutine(fade);
            if (target > 0f && !music.isPlaying) music.Play();
            fade = StartCoroutine(FadeTo(music, target, seconds));
        }

        IEnumerator FadeTo(AudioSource music, float target, float seconds)
        {
            float from = music.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                music.volume = Mathf.Lerp(from, target, t / seconds);
                yield return null;
            }
            music.volume = target;
            if (target <= 0f) music.Pause();
        }

        /// <summary>
        /// A star landing on the end card. Each one rings a step higher, so the
        /// three play as a little rising tune.
        /// </summary>
        public void PlayStar(int inARow)
        {
            float[] steps = { 1f, 1.122f, 1.26f, 1.335f, 1.498f };
            Play(chime, 0.5f, steps[Mathf.Clamp(inARow, 0, steps.Length - 1)]);
        }

        /// <summary>A button press.</summary>
        public void PlayTick() => Play(tick, 0.5f);

        /// <summary>Kick-off: one blast of the whistle, and the crowd settles in.</summary>
        public void PlayKickOff()
        {
            Play(whistle, 0.6f);
            StartCrowd();
        }

        /// <summary>The corner is swung in: the thump of the boot and a rising murmur.</summary>
        public void PlayCross()
        {
            Play(touch, 0.8f, 0.8f);
            SwellCrowd();
        }

        public void PlayJump() => Play(swoosh, 0.3f);

        /// <summary>Ball on forehead: a firmer, higher thud for a well-timed one.</summary>
        public void PlayHeader(float quality) => Play(thump, 0.8f, Mathf.Lerp(1.15f, 1.45f, quality));

        /// <summary>Off the keeper's gloves, or cleared by the defender.</summary>
        public void PlayBlock() => Play(knock, 0.6f, 0.7f);

        /// <summary>A goal: the goal sting over a big cheer.</summary>
        public void PlayGoal(bool perfect)
        {
            Play(goalSting, 0.6f);
            Play(cheer, perfect ? 0.6f : 0.45f);
            SwellCrowd();
        }

        /// <summary>Saved, missed or cleared: the crowd's "ohhh".</summary>
        public void PlayMiss() => Play(ohh, 0.5f);

        /// <summary>The round is over: the final whistle, and applause for a new best.</summary>
        public void PlayFinalWhistle(bool newBest)
        {
            StartCoroutine(FinalWhistle(newBest));
        }

        IEnumerator FinalWhistle(bool newBest)
        {
            Play(whistle, 0.55f);
            yield return new WaitForSeconds(0.35f);
            Play(whistle, 0.55f, 1.05f);
            if (!newBest) yield break;
            yield return new WaitForSeconds(0.9f);
            // Kept under the end-of-round music sting that lands just after.
            if (applause != null) Play(applause, 0.5f); else Play(cheer, 0.45f);
            SwellCrowd();
        }

        /// <summary>
        /// Called from the web page through SendMessage("HeaderAudio", "SetMuted", "1"/"0"),
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

        /// <summary>A bright two-note "ding-ding", like a coin in a cartoon.</summary>
        static AudioClip BuildChime()
        {
            return Create("Chime", 0.42f, (t, phase) =>
            {
                float frequency = t < 0.07f ? 987.77f : 1318.51f;   // B5, then E6
                float local = t < 0.07f ? t : t - 0.07f;
                float envelope = Mathf.Min(1f, local * 400f) * Mathf.Exp(-9f * local);
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t)
                           + 0.3f * Mathf.Sin(4f * Mathf.PI * frequency * t);
                return tone * envelope * 0.45f;
            });
        }

        /// <summary>A short, soft click for a button press.</summary>
        static AudioClip BuildTick()
        {
            return Create("Tick", 0.045f, (t, phase) =>
            {
                float tone = Mathf.Sin(2f * Mathf.PI * 1750f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 3500f * t);
                return tone * Mathf.Exp(-110f * t) * 0.5f;
            });
        }

        /// <summary>A hollow "tok", for the ball off the keeper's gloves or a defender.</summary>
        static AudioClip BuildKnock()
        {
            var random = new System.Random(11);
            return Create("Knock", 0.25f, (t, phase) =>
            {
                float body = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(420f, 260f, phase) * t) * Mathf.Exp(-28f * t);
                float click = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-120f * t);
                return (body * 0.8f + click * 0.4f) * 0.8f;
            });
        }

        /// <summary>A soft breath of air as he takes off.</summary>
        static AudioClip BuildSwoosh()
        {
            var random = new System.Random(5);
            float smooth = 0f;
            return Create("Swoosh", 0.2f, (t, phase) =>
            {
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                smooth = Mathf.Lerp(smooth, noise, 0.18f);
                float envelope = Mathf.Sin(Mathf.PI * phase);
                return smooth * envelope * 0.9f;
            });
        }

        /// <summary>The thump of a boot on the ball.</summary>
        static AudioClip BuildTouch()
        {
            var random = new System.Random(3);
            return Create("Touch", 0.09f, (t, phase) =>
            {
                float thud = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(190f, 110f, phase) * t);
                float scuff = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-90f * t);
                return (thud * 0.7f + scuff * 0.3f) * Mathf.Exp(-35f * t);
            });
        }

        /// <summary>A low, heavy thud: pitched up, the ball meeting a forehead.</summary>
        static AudioClip BuildThump()
        {
            var random = new System.Random(7);
            return Create("Thump", 0.3f, (t, phase) =>
            {
                float envelope = Mathf.Exp(-14f * t);
                float thud = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(120f, 45f, phase) * t);
                float crack = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-60f * t);
                return (thud * 0.85f + crack * 0.4f) * envelope;
            });
        }

        /// <summary>A rising three-note flourish, used if the cheer is missing.</summary>
        static AudioClip BuildFanfare()
        {
            float[] notes = { 523.25f, 659.25f, 783.99f };   // C5, E5, G5
            const float step = 0.11f;

            return Create("Fanfare", 0.55f, (t, phase) =>
            {
                int index = Mathf.Min(notes.Length - 1, (int)(t / step));
                float local = t - index * step;
                float envelope = Mathf.Exp(-7f * local) * (1f - phase * 0.25f);
                float tone = Mathf.Sin(2f * Mathf.PI * notes[index] * t)
                           + 0.35f * Mathf.Sin(4f * Mathf.PI * notes[index] * t);
                return tone * envelope * 0.5f;
            });
        }

        /// <summary>A short falling tone, used if the "ohh" is missing.</summary>
        static AudioClip BuildGroan()
        {
            return Create("Groan", 0.4f, (t, phase) =>
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
