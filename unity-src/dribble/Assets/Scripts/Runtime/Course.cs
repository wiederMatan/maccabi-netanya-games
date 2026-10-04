using System.Collections.Generic;
using UnityEngine;

namespace Dribble
{
    /// <summary>
    /// The pitch as a treadmill. The runner stays at z = 0 and everything else
    /// slides toward the camera: pitch segments leapfrog to the front once they are
    /// behind the camera, and defenders, cones and stars come out of fixed pools and
    /// go back to them, so a run of any length allocates nothing.
    /// </summary>
    public class Course : MonoBehaviour
    {
        public const float LaneWidth = 1.7f;
        public const float SegmentLength = 20f;
        /// <summary>New rows appear this far ahead, inside the fog so they fade in.</summary>
        public const float SpawnDistance = 68f;
        /// <summary>The first row of a run, close enough to reach in a few seconds.</summary>
        const float FirstRowDistance = 24f;
        /// <summary>Items are recycled once this far behind the runner.</summary>
        const float RecycleBehind = -6f;
        const float StarSpacing = 1.9f;
        const float StarHeight = 0.85f;

        [SerializeField] Transform[] segments;
        [SerializeField] PitchItem[] defenders;
        [SerializeField] PitchItem[] cones;
        [SerializeField] PitchItem[] stars;

        readonly List<PitchItem> active = new List<PitchItem>();
        System.Random random = new System.Random();
        float untilNextRow;

        public IReadOnlyList<PitchItem> Active => active;
        /// <summary>Items a row wanted but the pool had none spare. Checked by VerifyScene.</summary>
        public int Shortfalls { get; private set; }

        public static float LaneX(int lane) => (lane - 1) * LaneWidth;

        public void Bind(Transform[] pitchSegments, PitchItem[] defenderPool, PitchItem[] conePool, PitchItem[] starPool)
        {
            segments = pitchSegments;
            defenders = defenderPool;
            cones = conePool;
            stars = starPool;
        }

        void Awake()
        {
            foreach (var item in AllItems()) item.gameObject.SetActive(false);
        }

        IEnumerable<PitchItem> AllItems()
        {
            if (defenders != null) foreach (var d in defenders) if (d != null) yield return d;
            if (cones != null) foreach (var c in cones) if (c != null) yield return c;
            if (stars != null) foreach (var s in stars) if (s != null) yield return s;
        }

        /// <summary>Clear the pitch and lay out the opening rows of a new run.</summary>
        public void Begin(float speed)
        {
            random = new System.Random();

            foreach (var item in AllItems()) item.gameObject.SetActive(false);
            active.Clear();
            Shortfalls = 0;

            float z = FirstRowDistance;
            while (z < SpawnDistance)
            {
                float gap = RowGap(speed);
                SpawnRow(z, gap, speed);
                z += gap;
            }
            untilNextRow = z - SpawnDistance;
        }

        float RowGap(float speed)
        {
            // A little variety, never so tight a lane change cannot be made.
            float jitter = 0.85f + (float)random.NextDouble() * 0.35f;
            return Mathf.Max(7f, speed * Progression.PaceAt(speed).RowGapSeconds * jitter);
        }

        /// <summary>Slide the whole pitch toward the camera by <paramref name="distance"/> metres.</summary>
        public void Advance(float distance, float speed)
        {
            if (segments != null)
            {
                float span = SegmentLength * segments.Length;
                foreach (var segment in segments)
                {
                    var p = segment.localPosition;
                    p.z -= distance;
                    if (p.z < -SegmentLength) p.z += span;
                    segment.localPosition = p;
                }
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var item = active[i];
                if (!item.gameObject.activeSelf)
                {
                    active.RemoveAt(i);
                    continue;
                }

                var p = item.transform.localPosition;
                p.z -= distance;
                item.transform.localPosition = p;

                if (p.z < RecycleBehind)
                {
                    item.gameObject.SetActive(false);
                    active.RemoveAt(i);
                }
            }

            untilNextRow -= distance;
            while (untilNextRow <= 0f)
            {
                float gap = RowGap(speed);
                SpawnRow(SpawnDistance + untilNextRow, gap, speed);
                untilNextRow += gap;
            }
        }

        void SpawnRow(float z, float gapToNext, float speed)
        {
            var row = CourseGenerator.Next(Progression.PaceAt(speed), random);

            for (int lane = 0; lane < CourseGenerator.LaneCount; lane++)
            {
                switch (row.Lanes[lane])
                {
                    case Blocker.Defender:
                        Take(defenders, lane, new Vector3(LaneX(lane), 0f, z));
                        break;
                    case Blocker.Cone:
                        Take(cones, lane, new Vector3(LaneX(lane), 0f, z));
                        break;
                }
            }

            if (row.StarLane >= 0) StarLine(row.StarLane, z);
            if (row.BonusStarLane >= 0) StarLine(row.BonusStarLane, z + gapToNext * 0.5f);
        }

        void StarLine(int lane, float centreZ)
        {
            for (int i = -1; i <= 1; i++)
                Take(stars, lane, new Vector3(LaneX(lane), StarHeight, centreZ + i * StarSpacing));
        }

        void Take(PitchItem[] pool, int lane, Vector3 position)
        {
            if (pool == null) return;
            foreach (var item in pool)
            {
                if (item == null || item.gameObject.activeSelf) continue;
                item.Place(lane, position);
                active.Add(item);
                return;
            }
            // Pool exhausted: skipping one item is better than allocating mid-run.
            Shortfalls++;
        }
    }
}
