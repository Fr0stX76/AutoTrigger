using System;
using System.Collections.Generic;
using AutoTrigger.Core.Text;

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

        public Note Note { get; set; }
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
                string name = app.Name;
                issues.Add(new ValidationIssue
                {
                    Kind = IssueKind.MissingApplicationProfile,
                    ApplicationId = app.Id,
                    Note = new Note(() => Texts.IssueApplicationProfile(name)),
                });
            }
        }

        private static ValidationIssue CheckAction(AppSettings settings, Profile profile, EventKind kind, ProfileAction action)
        {
            string profileName = profile.Name;
            if (action is DisplayAction display && display.DisplayId != Guid.Empty
                && !settings.Displays.Exists(d => d.Id == display.DisplayId))
            {
                return new ValidationIssue
                {
                    Kind = IssueKind.MissingDisplay,
                    Note = new Note(() => Texts.IssueDisplay(profileName, EventText(kind))),
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
                    Note = new Note(() => Texts.IssueReference(profileName, EventText(kind))),
                };
            }
            if (target.Id == profile.Id || CanReach(settings, kind, target.Id, profile.Id))
            {
                string targetName = target.Name;
                return new ValidationIssue
                {
                    Kind = IssueKind.Loop,
                    Note = new Note(() => Texts.IssueLoop(profileName, EventText(kind), targetName)),
                };
            }
            return null;
        }

        public static string EventText(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Started: return Texts.EventStarted;
                case EventKind.Closed: return Texts.EventClosed;
                case EventKind.GotFocus: return Texts.EventGotFocus;
                default: return Texts.EventLostFocus;
            }
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
