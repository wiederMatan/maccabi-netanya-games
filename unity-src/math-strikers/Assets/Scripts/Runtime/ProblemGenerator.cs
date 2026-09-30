using System.Collections.Generic;
using UnityEngine;

namespace MathStrikers
{
    public enum Difficulty
    {
        /// <summary>Ages 8-9: sums and differences inside 20, never negative.</summary>
        Starter = 0,
        Rookie = 1,
        Pro = 2,
        Legend = 3
    }

    /// <summary>A single math question plus the multiple-choice answers shown on the goal.</summary>
    public struct MathProblem
    {
        public string Text;
        public int Answer;
        public int[] Options;
        public int CorrectIndex;
    }

    /// <summary>
    /// Builds arithmetic problems for a difficulty tier and a set of plausible
    /// wrong answers. Distractors sit near the real answer so the player has to
    /// actually compute rather than eyeball the magnitude.
    /// </summary>
    public static class ProblemGenerator
    {
        public const int OptionCount = 3;

        public static MathProblem Create(Difficulty difficulty)
        {
            int a, b, answer;
            string text;

            switch (difficulty)
            {
                case Difficulty.Starter:
                    // Small numbers a child can still count on their fingers if they
                    // need to, and subtraction never crosses zero.
                    if (Random.value < 0.5f)
                    {
                        a = Random.Range(1, 11);
                        b = Random.Range(1, Mathf.Min(11, 21 - a));
                        answer = a + b;
                        text = $"{a} + {b}";
                    }
                    else
                    {
                        a = Random.Range(5, 21);
                        b = Random.Range(1, a);
                        answer = a - b;
                        text = $"{a} - {b}";
                    }
                    break;

                case Difficulty.Rookie:
                    if (Random.value < 0.5f)
                    {
                        a = Random.Range(2, 41);
                        b = Random.Range(2, 41);
                        answer = a + b;
                        text = $"{a} + {b}";
                    }
                    else
                    {
                        a = Random.Range(10, 61);
                        b = Random.Range(1, a);
                        answer = a - b;
                        text = $"{a} - {b}";
                    }
                    break;

                case Difficulty.Pro:
                {
                    int pick = Random.Range(0, 3);
                    if (pick == 0)
                    {
                        a = Random.Range(10, 81);
                        b = Random.Range(10, 81);
                        answer = a + b;
                        text = $"{a} + {b}";
                    }
                    else if (pick == 1)
                    {
                        a = Random.Range(20, 91);
                        b = Random.Range(1, a);
                        answer = a - b;
                        text = $"{a} - {b}";
                    }
                    else
                    {
                        a = Random.Range(2, 13);
                        b = Random.Range(2, 13);
                        answer = a * b;
                        text = $"{a} × {b}";
                    }
                    break;
                }

                default:
                {
                    if (Random.value < 0.5f)
                    {
                        a = Random.Range(3, 16);
                        b = Random.Range(3, 13);
                        answer = a * b;
                        text = $"{a} × {b}";
                    }
                    else
                    {
                        b = Random.Range(2, 13);
                        answer = Random.Range(2, 16);
                        a = b * answer;
                        text = $"{a} ÷ {b}";
                    }
                    break;
                }
            }

            return BuildChoices(text, answer, difficulty);
        }

        static MathProblem BuildChoices(string text, int answer, Difficulty difficulty)
        {
            var options = new List<int>(OptionCount) { answer };
            int spread = difficulty switch
            {
                Difficulty.Legend => 12,
                Difficulty.Pro => 10,
                Difficulty.Rookie => 8,
                _ => 4  // Starter answers are small, so wrong options must sit close.
            };
            int guard = 0;

            while (options.Count < OptionCount && guard++ < 200)
            {
                // Half the time sit right next to the answer - off-by-one errors are
                // the mistake worth training against.
                int candidate = Random.value < 0.4f
                    ? answer + (Random.value < 0.5f ? 1 : -1)
                    : answer + Random.Range(-spread, spread + 1);

                if (candidate < 0 || options.Contains(candidate)) continue;
                options.Add(candidate);
            }

            // Pad defensively in case the guard tripped on a tiny answer.
            int filler = answer + 1;
            while (options.Count < OptionCount)
            {
                if (!options.Contains(filler)) options.Add(filler);
                filler++;
            }

            Shuffle(options);

            return new MathProblem
            {
                Text = text,
                Answer = answer,
                Options = options.ToArray(),
                CorrectIndex = options.IndexOf(answer)
            };
        }

        static void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
