using System;
using System.Collections.Generic;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public sealed class PlannedAction
    {
        public ProfileAction Action { get; set; }

        public int DelayMs { get; set; }

        public string Source { get; set; }
    }

    public static class PlanBuilder
    {
        public const int MaxDepth = 8;

        public static List<PlannedAction> Build(AppSettings settings, Profile profile, EventKind kind)
        {
            var result = new List<PlannedAction>();
            if (profile == null)
                return result;
            var path = new List<Guid>();
            long carry = 0;
            Visit(settings, profile, kind, 0, path, profile.Name, result, ref carry);
            return result;
        }

        private static void Visit(AppSettings settings, Profile profile, EventKind kind, int depth,
            List<Guid> path, string source, List<PlannedAction> result, ref long carry)
        {
            path.Add(profile.Id);
            try
            {
                foreach (ProfileAction action in profile.For(kind))
                {
                    carry += action.DelayMs;
                    if (action is IncludeProfileAction include)
                    {
                        Profile target = settings.FindProfile(include.ProfileId);
                        if (target == null)
                        {
                            Log.Error($"{source}: 'Reference profile' points to a profile that does not exist; skipped.");
                            continue;
                        }
                        if (path.Contains(target.Id))
                        {
                            Log.Error($"{source}: 'Reference profile' to '{target.Name}' makes a loop; skipped.");
                            continue;
                        }
                        if (depth + 1 > MaxDepth)
                        {
                            Log.Error($"{source}: more than {MaxDepth} levels of 'Reference profile'; '{target.Name}' skipped.");
                            continue;
                        }
                        Visit(settings, target, kind, depth + 1, path, source + " > " + target.Name, result, ref carry);
                        continue;
                    }

                    result.Add(new PlannedAction
                    {
                        Action = action,
                        DelayMs = (int)Math.Min(carry, int.MaxValue),
                        Source = source,
                    });
                    carry = 0;
                }
            }
            finally
            {
                path.RemoveAt(path.Count - 1);
            }
        }
    }
}
