using System;
using System.Collections;
using UnityEngine;

namespace PenaltyDuel
{
    /// <summary>
    /// Drives the ball: sits on the spot, gets struck toward an aim point with a
    /// ballistic arc - or into the keeper's gloves and back out - and reports when
    /// the strike has resolved.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallController : MonoBehaviour
    {
        [SerializeField] float flightTime = 0.7f;
        [SerializeField] float spinTorque = 14f;

        Rigidbody body;
        Vector3 spot;
        Coroutine resolveRoutine;

        public bool InFlight { get; private set; }

        /// <summary>Raised once the ball has finished its strike (scored, saved or wide).</summary>
        public event Action Resolved;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            spot = transform.position;
            Park();
        }

        /// <summary>Return the ball to the spot and freeze it there.</summary>
        public void Park()
        {
            if (resolveRoutine != null)
            {
                StopCoroutine(resolveRoutine);
                resolveRoutine = null;
            }

            InFlight = false;

            // Zero the motion while the body is still dynamic - a kinematic body
            // rejects velocity writes and Unity logs a warning for each one.
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.isKinematic = true;
            // Move the body as well as the transform: with interpolation on, the
            // body would otherwise drag the ball back to wherever it came to rest.
            body.position = spot;
            body.rotation = Quaternion.identity;
            transform.position = spot;
            transform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// Strike the ball so it passes through <paramref name="aimPoint"/>, solving the
        /// launch velocity from the fixed flight time so placement always reads true.
        /// </summary>
        public void Strike(Vector3 aimPoint, bool parried = false)
        {
            Park();

            InFlight = true;
            body.isKinematic = false;

            Vector3 delta = aimPoint - transform.position;
            Vector3 gravity = Physics.gravity;

            // p = v*t + 0.5*g*t^2  ->  v = (p - 0.5*g*t^2) / t
            Vector3 velocity = (delta - 0.5f * gravity * flightTime * flightTime) / flightTime;

            body.linearVelocity = velocity;
            body.AddTorque(new Vector3(velocity.z, 0f, -velocity.x).normalized * spinTorque,
                ForceMode.VelocityChange);

            resolveRoutine = StartCoroutine(ResolveAfter(flightTime + 0.85f, parried));
        }

        IEnumerator ResolveAfter(float seconds, bool parried)
        {
            if (parried)
            {
                // The keeper gets there just as the ball does: knock it back out
                // and away from the goal, off to the side he dived to.
                yield return new WaitForSeconds(flightTime);
                float side = transform.position.x >= 0f ? 1f : -1f;
                body.linearVelocity = new Vector3(side * 2.2f, 2.6f, -5.5f);
                body.angularVelocity = Vector3.zero;
                seconds -= flightTime;
            }
            else
            {
                // The net takes the pace off: without this the ball rebounds off the
                // back of the goal and rolls all the way back out.
                yield return new WaitForSeconds(flightTime + 0.12f);
                body.linearVelocity *= 0.15f;
                seconds -= flightTime + 0.12f;
            }

            yield return new WaitForSeconds(seconds);
            InFlight = false;
            resolveRoutine = null;
            Resolved?.Invoke();
        }
    }
}
