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

        // In a filtered list, an item trades places with the next one shown, past the hidden ones in between.
        public static bool MoveAmong<T>(List<T> items, T item, int step, Func<T, bool> shown)
        {
            int index = items.IndexOf(item);
            if (index < 0 || step == 0)
                return false;
            int direction = Math.Sign(step);
            int left = Math.Abs(step);
            int target = index;
            for (int i = index + direction; i >= 0 && i < items.Count && left > 0; i += direction)
            {
                if (!shown(items[i]))
                    continue;
                target = i;
                left--;
            }
            if (left > 0)
                return false;
            items.RemoveAt(index);
            items.Insert(target, item);
            return true;
        }
    }
}
