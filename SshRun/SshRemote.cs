using System;

namespace SshRun
{
    public static class SshRemote
    {
        /// <summary>
        ///     The path of the '.sshrun' folder created by the invocation function. Only
        ///     available if the current process is running remotely.
        /// </summary>
        public static string Path =>
            Program.SshRunPath ??
            throw new InvalidOperationException("SshRemote.Path is only available when running remotely.");

        /// <summary>
        ///     Returns a path to a non-existent file in the .sshrun folder of the 
        ///     current execution. This file can then be created by the process and returned
        ///     to the invocation function.
        /// </summary>
        public static RemoteFile GetFreshFile()
        {
            var sshrun = Program.SshRunPath ??
                throw new InvalidOperationException("SshRemote.GetFreshFile is only available when running remotely.");

            return new($"{sshrun}/{Guid.NewGuid()}");
        }
    }
}
