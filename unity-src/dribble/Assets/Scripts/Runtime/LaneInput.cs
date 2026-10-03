using UnityEngine;

namespace Dribble
{
    /// <summary>
    /// Turns every way a child might steer into "one lane left" or "one lane right":
    /// a swipe (acted on as soon as the finger has moved far enough, not on
    /// release), a tap on the left or right half of the screen, a mouse drag or
    /// click, or the arrow / A-D keys.
    /// </summary>
    public class LaneInput
    {
        // A swipe is this fraction of the shorter screen side - about a thumb's flick.
        const float SwipeFraction = 0.06f;
        // A press that moves less than this and lifts quickly is a tap.
        const float TapSeconds = 0.4f;
        // Browsers fire mouse events after a touch; ignore the mouse for a moment
        // after any touch so one tap is not counted twice.
        const float MouseQuietAfterTouch = 0.8f;

        Vector2 pressStart;
        float pressTime;
        bool pressed;
        bool pressUsed;
        int touchId = -1;
        float lastTouchTime = -10f;

        /// <summary>Forget any press in progress, e.g. the tap that pressed Start.</summary>
        public void Reset()
        {
            pressed = false;
            pressUsed = true;
            touchId = -1;
        }

        /// <summary>-1 for left, +1 for right, 0 for nothing this frame.</summary>
        public int Poll()
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) return -1;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) return 1;

            if (Input.touchCount > 0)
            {
                lastTouchTime = Time.unscaledTime;
                return PollTouches();
            }

            if (Time.unscaledTime - lastTouchTime < MouseQuietAfterTouch) return 0;
            return PollMouse();
        }

        int PollTouches()
        {
            int result = 0;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        // A new finger takes over; a quick second thumb is a fresh press.
                        touchId = touch.fingerId;
                        Begin(touch.position);
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (touch.fingerId == touchId && result == 0) result = Drag(touch.position);
                        break;
                    case TouchPhase.Ended:
                        if (touch.fingerId == touchId)
                        {
                            if (result == 0) result = Release(touch.position);
                            touchId = -1;
                        }
                        break;
                    case TouchPhase.Canceled:
                        if (touch.fingerId == touchId) { pressed = false; touchId = -1; }
                        break;
                }
            }
            return result;
        }

        int PollMouse()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Begin(Input.mousePosition);
                return 0;
            }
            if (Input.GetMouseButton(0)) return Drag(Input.mousePosition);
            if (Input.GetMouseButtonUp(0)) return Release(Input.mousePosition);
            return 0;
        }

        void Begin(Vector2 position)
        {
            pressed = true;
            pressUsed = false;
            pressStart = position;
            pressTime = Time.unscaledTime;
        }

        int Drag(Vector2 position)
        {
            if (!pressed || pressUsed) return 0;
            Vector2 delta = position - pressStart;
            float threshold = Mathf.Min(Screen.width, Screen.height) * SwipeFraction;
            if (Mathf.Abs(delta.x) < threshold || Mathf.Abs(delta.x) < Mathf.Abs(delta.y)) return 0;
            pressUsed = true;
            return delta.x > 0f ? 1 : -1;
        }

        int Release(Vector2 position)
        {
            if (!pressed) return 0;
            pressed = false;
            if (pressUsed) return 0;

            int swipe = Drag(position);
            if (swipe != 0) return swipe;

            Vector2 delta = position - pressStart;
            float threshold = Mathf.Min(Screen.width, Screen.height) * SwipeFraction;
            if (Time.unscaledTime - pressTime > TapSeconds || delta.magnitude > threshold) return 0;
            return position.x < Screen.width * 0.5f ? -1 : 1;
        }
    }
}
