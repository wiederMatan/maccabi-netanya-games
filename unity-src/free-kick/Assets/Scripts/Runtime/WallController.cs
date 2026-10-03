using System.Collections;
using UnityEngine;

namespace FreeKick
{
    /// <summary>
    /// The defensive wall: a line of opposition players 9.15 m from the ball. The
    /// level decides how many stand in it and how tall they are; on the harder levels
    /// they jump as the ball is struck.
    /// </summary>
    public class WallController : MonoBehaviour
    {
        [SerializeField] Transform[] players;

        Vector3[] baseScales;
        Coroutine jump;
        int count;

        public int Capacity => players == null ? 0 : players.Length;
        public Transform[] Players => players;

        public void Bind(Transform[] wallPlayers) => players = wallPlayers;

        void Awake()
        {
            baseScales = new Vector3[Capacity];
            for (int i = 0; i < Capacity; i++) baseScales[i] = players[i].localScale;
        }

        /// <summary>Line up <see cref="Defence.WallCount"/> players at the defence's spot, facing the ball.</summary>
        public void LineUp(in Defence defence, float scale, Vector3 ball)
        {
            if (jump != null) StopCoroutine(jump);
            jump = null;
            count = Mathf.Min(defence.WallCount, Capacity);

            for (int i = 0; i < Capacity; i++)
            {
                var player = players[i];
                bool active = i < count;
                player.gameObject.SetActive(active);
                if (!active) continue;

                player.localScale = baseScales[i] * scale;
                var spot = new Vector3(defence.WallPlayerX(i), 0f, defence.WallZ);
                player.position = spot;
                Vector3 look = ball - spot;
                look.y = 0f;
                player.rotation = Quaternion.LookRotation(look);
            }
        }

        /// <summary>Everyone jumps together, peaking about when the ball arrives.</summary>
        public void Jump(float height)
        {
            if (height <= 0f) return;
            if (jump != null) StopCoroutine(jump);
            jump = StartCoroutine(JumpRoutine(height));
        }

        IEnumerator JumpRoutine(float height)
        {
            const float duration = 0.7f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float y = height * Mathf.Sin(Mathf.PI * t / duration);
                for (int i = 0; i < count; i++)
                {
                    var p = players[i].position;
                    players[i].position = new Vector3(p.x, y, p.z);
                }
                yield return null;
            }
            for (int i = 0; i < count; i++)
            {
                var p = players[i].position;
                players[i].position = new Vector3(p.x, 0f, p.z);
            }
            jump = null;
        }

        /// <summary>A little flinch from the player the ball hit.</summary>
        public void Flinch(int index)
        {
            if (index < 0 || index >= count) return;
            StartCoroutine(FlinchRoutine(players[index]));
        }

        static IEnumerator FlinchRoutine(Transform player)
        {
            Quaternion upright = player.rotation;
            Quaternion bent = upright * Quaternion.Euler(-18f, 0f, 0f);
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                player.rotation = Quaternion.Slerp(bent, upright, t / 0.5f);
                yield return null;
            }
            player.rotation = upright;
        }
    }
}
