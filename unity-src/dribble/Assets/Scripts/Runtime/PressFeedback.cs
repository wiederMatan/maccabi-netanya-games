using MaccabiShared;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Dribble
{
    /// <summary>
    /// Makes a button feel pressed: it sinks to 94% while held and springs back
    /// on release, with a tick through the game's audio (so the page's mute
    /// silences it too) and a short buzz on phones that support it.
    /// </summary>
    public class PressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        const float PressedScale = 0.94f;
        const float DownSeconds = 0.08f;
        const float UpSeconds = 0.14f;

        float target = 1f;
        float from = 1f;
        float started;
        float duration = DownSeconds;

        public void OnPointerDown(PointerEventData eventData)
        {
            Go(PressedScale, DownSeconds);
            DribbleAudio.Instance?.PlayTick();
            PortalBridge.Haptic(10);
        }

        public void OnPointerUp(PointerEventData eventData) => Go(1f, UpSeconds);
        public void OnPointerExit(PointerEventData eventData) => Go(1f, UpSeconds);

        void OnDisable()
        {
            target = from = 1f;
            transform.localScale = Vector3.one;
        }

        void Go(float scale, float seconds)
        {
            from = transform.localScale.x;
            target = scale;
            duration = seconds;
            started = Time.unscaledTime;
        }

        void Update()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - started) / duration);
            // Ease out, so the press lands quickly and the release settles.
            float eased = 1f - (1f - t) * (1f - t);
            float scale = Mathf.LerpUnclamped(from, target, eased);
            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
