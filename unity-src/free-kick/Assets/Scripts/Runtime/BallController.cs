using System;
using System.Collections;
using UnityEngine;

namespace FreeKick
{
    /// <summary>
    /// Drives the ball: sits on the spot, flies the scripted <see cref="ShotPath"/>
    /// to whatever the judge decided - the wall, the keeper's gloves, the woodwork or
    /// the net - and then hands over to physics so the rebound looks natural.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallController : MonoBehaviour
    {
        [SerializeField] float settleSeconds = 1.3f;

        Rigidbody body;
        Coroutine flight;

        public bool InFlight { get; private set; }

        /// <summary>Raised when the ball reaches the moment the judge decided on.</summary>
        public event Action<Outcome> Arrived;

        /// <summary>Raised once the ball has settled after the shot.</summary>
        public event Action Resolved;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            Freeze();
        }

        /// <summary>Put the ball down on a spot and hold it there.</summary>
        public void Place(Vector3 spot)
        {
            if (flight != null)
            {
                StopCoroutine(flight);
                flight = null;
            }

            InFlight = false;
            Freeze();
            transform.position = new Vector3(spot.x, Pitch.BallRadius, spot.z);
            transform.rotation = Quaternion.identity;
        }

        void Freeze()
        {
            // Zero the motion while the body is still dynamic - a kinematic body
            // rejects velocity writes and Unity logs a warning for each one.
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
        }

        public void Strike(ShotPath path, Outcome outcome)
        {
            if (flight != null) StopCoroutine(flight);
            flight = StartCoroutine(Fly(path, outcome));
        }

        IEnumerator Fly(ShotPath path, Outcome outcome)
        {
            InFlight = true;
            body.isKinematic = true;

            // Spin about the axis the bend implies, so a curler visibly curls.
            Vector3 spinAxis = (Vector3.right + Vector3.up * Mathf.Clamp(path.Bend, -1f, 1f) * 0.8f).normalized;
            float end = Mathf.Clamp01(outcome.At);
            float t = 0f;

            while (true)
            {
                t += Time.deltaTime;
                float s = Mathf.Min(t / path.FlightTime, end);
                transform.position = path.Evaluate(s);
                transform.Rotate(spinAxis, 900f * Time.deltaTime, Space.World);
                if (s >= end) break;
                yield return null;
            }

            transform.position = path.Evaluate(end);
            Arrived?.Invoke(outcome);

            body.isKinematic = false;
            body.linearVelocity = Rebound(path, outcome);
            body.angularVelocity = UnityEngine.Random.insideUnitSphere * 8f;

            yield return new WaitForSeconds(settleSeconds);
            InFlight = false;
            flight = null;
            Resolved?.Invoke();
        }

        /// <summary>How the ball leaves whatever it met.</summary>
        static Vector3 Rebound(ShotPath path, Outcome outcome)
        {
            Vector3 v = path.Velocity(outcome.At);
            switch (outcome.Result)
            {
                case ShotResult.Blocked:
                    // Off a body in the wall: back toward the taker, popping up.
                    return new Vector3(v.x * 0.2f + UnityEngine.Random.Range(-1.5f, 1.5f), 3.2f, -v.z * 0.3f);
                case ShotResult.Saved:
                    // Parried out wide of the post.
                    float side = Mathf.Sign(v.x == 0f ? UnityEngine.Random.value - 0.5f : v.x);
                    return new Vector3(side * 4.5f, 2.5f, -3.5f);
                case ShotResult.Woodwork:
                    return new Vector3(v.x * 0.3f, Mathf.Abs(v.y) * 0.4f + 1f, -v.z * 0.45f);
                default:
                    // Goal, over or wide: carry on and let the net or the floor stop it.
                    return v;
            }
        }
    }
}
