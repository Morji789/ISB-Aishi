using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AishiKeys
{
    internal static class TapeStashTraversal
    {
        internal const int MaximumDepth = 64;
        internal static bool IsLooseInStash<T>(T item, T stash, Func<T, T> parent, Func<T, bool> isWeapon)
            where T : class
        {
            if (item == null || stash == null)
                return false;
            var visited = new HashSet<T>(Identity<T>.Instance);
            for (int depth = 0; item != null && depth < MaximumDepth; depth++)
            {
                if (!visited.Add(item) || isWeapon(item))
                    return false;
                if (ReferenceEquals(item, stash))
                    return true;
                item = parent(item);
            }

            return false;
        }

        private sealed class Identity<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly Identity<T> Instance = new Identity<T>();
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T item) => RuntimeHelpers.GetHashCode(item);
        }
    }
}
