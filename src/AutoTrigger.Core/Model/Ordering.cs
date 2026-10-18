using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTrigger.Core.Model
{
    public static class Ordering
    {
        public static void SortByName<T>(List<T> items, Func<T, string> name, bool descending)
        {
            List<T> sorted = descending
                ? items.OrderByDescending(i => name(i) ?? string.Empty, StringComparer.CurrentCultureIgnoreCase).ToList()
                : items.OrderBy(i => name(i) ?? string.Empty, StringComparer.CurrentCultureIgnoreCase).ToList();
            items.Clear();
            items.AddRange(sorted);
        }

        public static bool Move<T>(List<T> items, T item, int step)
        {
            int index = items.IndexOf(item);
            int target = index + step;
            if (index < 0 || target < 0 || target >= items.Count)
                return false;
            items.RemoveAt(index);
            items.Insert(target, item);
            return true;
        }
    }
}
