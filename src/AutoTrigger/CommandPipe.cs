using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AutoTrigger
{
    internal static class CommandPipe
    {
        public static NamedPipeServerStream CreateSecured(string name)
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(AllowedSid(), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            return new NamedPipeServerStream(name, PipeDirection.InOut, CommandServer.MaxInstances, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 0, 0, security);
        }

        private static SecurityIdentifier AllowedSid()
        {
            using (WindowsIdentity user = WindowsIdentity.GetCurrent())
            {
                bool elevated = new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator);
                return elevated ? new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) : user.User;
            }
        }
    }
}
