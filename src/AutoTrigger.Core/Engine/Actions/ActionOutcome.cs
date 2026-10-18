using System.Threading.Tasks;

namespace AutoTrigger.Core.Engine
{
    public enum ActionResult
    {
        Succeeded,
        Failed,
        Skipped,
        Cancelled,
    }

    public sealed class ActionOutcome
    {
        public ActionResult Result { get; private set; }

        public string Message { get; private set; }

        public Task ProgramExited { get; private set; }

        public static ActionOutcome Success(string message, Task programExited = null) =>
            new ActionOutcome { Result = ActionResult.Succeeded, Message = message, ProgramExited = programExited };

        public static ActionOutcome Failure(string message) => new ActionOutcome { Result = ActionResult.Failed, Message = message };

        public static ActionOutcome Skip(string message) => new ActionOutcome { Result = ActionResult.Skipped, Message = message };

        public static ActionOutcome Cancel(string message) => new ActionOutcome { Result = ActionResult.Cancelled, Message = message };
    }
}
