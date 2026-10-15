using System;
using System.Collections;
using UnityEngine;

namespace Headers
{
    /// <summary>
    /// The ball flies scripted <see cref="Flight"/>s - the corner to the meeting
    /// point, then the header to the goal - and is handed to physics afterwards,
    /// so it settles in the net, bounces off the keeper or rolls away naturally.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Ball : MonoBehaviour
    {
        Rigidbody body;
        Coroutine flight;
        Vector3 spinAxis = Vector3.right;
        float spin = 720f;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            Freeze();
        }

        /// <summary>Put the ball down on a spot and hold it there.</summary>
        public void Place(Vector3 spot)
        {
            Stop();
            Freeze();
            transform.SetPositionAndRotation(spot, Quaternion.identity);
        }

        public void Stop()
        {
            if (flight != null) StopCoroutine(flight);
            flight = null;
        }

        void Freeze()
        {
            // Zero the motion while the body is still dynamic: a kinematic body
            // rejects velocity writes and Unity logs a warning for each one.
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
        }

        /// <summary>
        /// Fly <paramref name="path"/> from its start for <paramref name="seconds"/>
        /// (its whole length if omitted), then call <paramref name="arrived"/>.
        /// </summary>
        public void Fly(Flight path, float seconds, Action arrived)
        {
            Stop();
            Freeze();
            var v = path.Launch;
            // Spin about the axis across the line of flight, so the panels roll forward.
            spinAxis = Vector3.Cross(Vector3.up, new Vector3(v.x, 0f, v.z)).normalized;
            if (spinAxis.sqrMagnitude < 0.01f) spinAxis = Vector3.right;
            spin = Mathf.Clamp(v.magnitude * 70f, 400f, 1100f);
            flight = StartCoroutine(Flying(path, seconds, arrived));
        }

        IEnumerator Flying(Flight path, float seconds, Action arrived)
        {
            float end = seconds > 0f ? seconds : path.Time;
            float t = 0f;
            while (t < end)
            {
                t = Mathf.Min(end, t + Time.deltaTime);
                transform.position = path.At(t);
                transform.Rotate(spinAxis, spin * Time.deltaTime, Space.World);
                if (t >= end) break;
                yield return null;
            }
            flight = null;
            arrived?.Invoke();
        }

        /// <summary>Let physics take over from here, moving at <paramref name="velocity"/>.</summary>
        public void Release(Vector3 velocity)
        {
            Stop();
            body.isKinematic = false;
            body.linearVelocity = velocity;
            body.angularVelocity = spinAxis * (spin * Mathf.Deg2Rad);
        }
    }
}
