using System;

namespace ACLS.Sim
{
    // Sim-internal RNG. System.Random keeps Sim independent of UnityEngine.
    // Replaceable via Seed() so playthroughs become reproducible later.
    public static class Rng
    {
        private static Random rng = new Random();

        public static void Seed(int seed) => rng = new Random(seed);

        // Inclusive on both ends, matching UnityEngine.Random.Range for ints.
        public static int Range(int minInclusive, int maxInclusive) =>
            rng.Next(minInclusive, maxInclusive + 1);

        public static bool Chance(int percent) => rng.Next(100) < percent;

        public static double NextDouble() => rng.NextDouble();
    }
}
