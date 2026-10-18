using System;
using System.Collections.Generic;

namespace AutoTrigger.Core.Model
{
    public enum IssueKind
    {
        MissingProfile,

        Loop,

        MissingApplicationProfile,

        MissingDisplay,

        MissingPowerPlan,
    }

    public sealed class ValidationIssue
    {
        public IssueKind Kind { get; set; }

        public Guid ProfileId { get; set; }

        public Guid ApplicationId { get; set; }

        public EventKind Event { get; set; }

        public int ActionIndex { get; set; } = -1;

        public string Message { get; set; }
    }

    public static class SettingsValidator
    {
        public static List<ValidationIssue> Validate(AppSettings settings)
        {
            var issues = new List<ValidationIssue>();
            CheckApplications(settings, issues);
            foreach (EventKind kind in Enum.GetValues(typeof(EventKind)))
            {
                foreach (Profile profile in settings.Profiles)
                {
                    List<ProfileAction> actions = profile.For(kind);
                    for (int i = 0; i < actions.Count; i++)
                    {
                        ValidationIssue issue = CheckAction(settings, profile, kind, actions[i]);
                        if (issue == null)
                            continue;
                        issue.ProfileId = profile.Id;
                        issue.Event = kind;
                        issue.ActionIndex = i;
                        issues.Add(issue);
                    }
                }
            }
            return issues;
        }

        private static void CheckApplications(AppSettings settings, List<ValidationIssue> issues)
        {
            foreach (AppEntry app in settings.Applications)
            {
                if (app.ProfileId == Guid.Empty || settings.FindProfile(app.ProfileId) != null)
                    continue;
                issues.Add(new ValidationIssue
                {
                    Kind = IssueKind.MissingApplicationProfile,
                    ApplicationId = app.Id,
                    Message = $"Application '{app.Name}' is assigned to a profile that does not exist.",
                });
            }
        }

        private static ValidationIssue CheckAction(AppSettings settings, Profile profile, EventKind kind, ProfileAction action)
        {
            if (action is DisplayAction display && display.DisplayId != Guid.Empty
                && !settings.Displays.Exists(d => d.Id == display.DisplayId))
            {
                return new ValidationIssue
                {
                    Kind = IssueKind.MissingDisplay,
                    Message = $"Profile '{profile.Name}' ({kind}): display action for a display that is no longer in the list.",
                };
            }
            if (!(action is IncludeProfileAction include))
                return null;

            Profile target = settings.FindProfile(include.ProfileId);
            if (target == null)
            {
                return new ValidationIssue
                {
                    Kind = IssueKind.MissingProfile,
                    Message = $"Profile '{profile.Name}' ({kind}): referenced profile does not exist.",
                };
            }
            if (target.Id == profile.Id || CanReach(settings, kind, target.Id, profile.Id))
            {
                return new ValidationIssue
                {
                    Kind = IssueKind.Loop,
                    Message = $"Profile '{profile.Name}' ({kind}): reference to '{target.Name}' makes a loop.",
                };
            }
            return null;
        }

        public static bool CanReach(AppSettings settings, EventKind kind, Guid from, Guid to)
        {
            var seen = new HashSet<Guid>();
            var stack = new Stack<Guid>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                Guid id = stack.Pop();
                if (id == to)
                    return true;
                if (!seen.Add(id))
                    continue;
                Profile profile = settings.FindProfile(id);
                if (profile == null)
                    continue;
                foreach (var action in profile.For(kind))
                {
                    if (action is IncludeProfileAction include && include.ProfileId != Guid.Empty)
                        stack.Push(include.ProfileId);
                }
            }
            return false;
        }

        public static List<Profile> AllowedReferences(AppSettings settings, Profile profile, EventKind kind)
        {
            var result = new List<Profile>();
            foreach (var candidate in settings.Profiles)
            {
                if (candidate.Id == profile.Id)
                    continue;
                if (CanReach(settings, kind, candidate.Id, profile.Id))
                    continue;
                result.Add(candidate);
            }
            return result;
        }
    }
}
