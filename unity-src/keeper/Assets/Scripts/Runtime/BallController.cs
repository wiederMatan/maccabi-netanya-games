using UnityEngine;

namespace Keeper
{
    /// <summary>
    /// Drives the ball: sits on the penalty spot, gets struck so it crosses the goal
    /// line at the target spot after an exact flight time, and is either parried
    /// back out by the keeper or left to bulge the net.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallController : MonoBehaviour
    {
        [SerializeField] float spinTorque = 14f;

        Rigidbody body;
        Vector3 spot;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            spot = transform.position;
            Park();
        }

        /// <summary>Return the ball to the penalty spot and freeze it there.</summary>
        public void Park()
        {
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
        /// Strike the ball so it passes through <paramref name="aimPoint"/> after
        /// <paramref name="flightTime"/> seconds, solving the launch velocity from the
        /// fixed time so the shot arrives exactly when the game says it does.
        /// </summary>
        public void Strike(Vector3 aimPoint, float flightTime)
        {
            Park();
            body.isKinematic = false;

            Vector3 delta = aimPoint - transform.position;
            // p = v*t + 0.5*g*t^2  ->  v = (p - 0.5*g*t^2) / t
            Vector3 velocity = (delta - 0.5f * Physics.gravity * flightTime * flightTime) / flightTime;

            body.linearVelocity = velocity;
            body.AddTorque(new Vector3(velocity.z, 0f, -velocity.x).normalized * spinTorque,
                ForceMode.VelocityChange);
        }

        /// <summary>The keeper gets a glove to it: knock the ball back out and wide.</summary>
        public void Parry(int side)
        {
            if (body.isKinematic) return;

            float sideways = side == 0 ? Random.Range(-1.5f, 1.5f) : side * Random.Range(2.5f, 4f);
            body.linearVelocity = new Vector3(sideways, Random.Range(2.5f, 4f), Random.Range(4.5f, 6.5f));
            body.angularVelocity = Random.insideUnitSphere * 12f;
        }
    }
}
