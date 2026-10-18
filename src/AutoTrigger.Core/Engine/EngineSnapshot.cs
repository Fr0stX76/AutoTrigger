using System;
using System.Collections.Generic;

namespace AutoTrigger.Core.Engine
{
    public sealed class AppStatus
    {
        public AppStatus(Guid id, string name, bool running, bool ignored, bool hasFocus)
        {
            Id = id;
            Name = name;
            Running = running;
            Ignored = ignored;
            HasFocus = hasFocus;
        }

        public Guid Id { get; }

        public string Name { get; }

        public bool Running { get; }

        public bool Ignored { get; }

        public bool HasFocus { get; }
    }

    public sealed class EngineSnapshot
    {
        public static readonly EngineSnapshot Empty = new EngineSnapshot(false, false, new AppStatus[0], Guid.Empty, null, Guid.Empty, null);

        public EngineSnapshot(bool monitoring, bool paused, IReadOnlyList<AppStatus> apps, Guid currentAppId,
            string currentAppName, Guid currentProfileId, string currentProfileName)
        {
            Monitoring = monitoring;
            Paused = paused;
            Apps = apps;
            CurrentAppId = currentAppId;
            CurrentAppName = currentAppName;
            CurrentProfileId = currentProfileId;
            CurrentProfileName = currentProfileName;
        }

        public bool Monitoring { get; }

        public bool Paused { get; }

        public IReadOnlyList<AppStatus> Apps { get; }

        public Guid CurrentAppId { get; }

        public string CurrentAppName { get; }

        public Guid CurrentProfileId { get; }

        public string CurrentProfileName { get; }
    }
}
