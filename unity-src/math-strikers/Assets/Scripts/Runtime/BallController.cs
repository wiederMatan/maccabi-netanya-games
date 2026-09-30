using System;
using System.Collections;
using UnityEngine;

namespace MathStrikers
{
    /// <summary>
    /// Drives the ball: sits on the spot, gets struck toward an aim point with a
    /// ballistic arc, and reports when the strike has resolved.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallController : MonoBehaviour
    {
        [SerializeField] float flightTime = 0.85f;
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
            transform.position = spot;
            transform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// Strike the ball so it passes through <paramref name="aimPoint"/>, solving the
        /// launch velocity from the fixed flight time so placement always reads true.
        /// </summary>
        public void Strike(Vector3 aimPoint)
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

            resolveRoutine = StartCoroutine(ResolveAfter(flightTime + 0.85f));
        }

        IEnumerator ResolveAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            InFlight = false;
            resolveRoutine = null;
            Resolved?.Invoke();
        }
    }
}
