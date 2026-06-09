using System;
using System.Collections.Generic;

namespace Game.Core.Util
{
    public class GameRng
    {
        private readonly System.Random _rng;
        public int Seed { get; }

        public GameRng(int seed)
        {
            Seed = seed;
            _rng = new System.Random(seed);
        }
        public int Next(int maxExclusive) => _rng.Next(maxExclusive);

        public int Next(int min, int maxExclusive) => _rng.Next(min, maxExclusive);
        public double NextDouble() => _rng.NextDouble();
        public bool Chance(double p) => _rng.NextDouble() < p;

        public T Pick<T>(IList<T> list)
        {
            if (list == null || list.Count == 0)
                throw new InvalidOperationException("Cannot pick from empty list");
            return list[_rng.Next(list.Count)];
        }

        public List<T> PickN<T>(IList<T> list, int n)
        {
            var copy = new List<T>(list);
            Shuffle(copy);
            return copy.GetRange(0, Math.Min(n, copy.Count));
        }
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}