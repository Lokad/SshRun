using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SshRun
{
    public interface IExecutionTarget : IDisposable
    {
        /// <summary> The root path inside which all files should be pushed. </summary>
        string RootPath { get; }

        /// <summary>
        ///     Writes the provided bytes to an absolute path on the execution 
        ///     target.
        /// </summary>
        Task WriteFileAsync(
            byte[] fileContents,
            RemoteFile file,
            CancellationToken cancel);

        /// <summary>
        ///     Writes the provided file to an absolute path on the execution 
        ///     target.
        /// </summary>
        Task WriteFileAsync(
            FileInfo local,
            RemoteFile file,
            CancellationToken cancel);

        /// <summary>
        ///     Copies the provided stream, in its entirety, to an absolute path 
        ///     on the execution target.
        /// </summary>
        Task WriteFileAsync(
            Stream fileContents,
            RemoteFile file,
            CancellationToken cancel);

        /// <summary> Read a remote file into the provided local stream. </summary>
        Task ReadFileAsync(
            RemoteFile file,
            Stream local,
            CancellationToken cancel);

        /// <summary>
        ///     Executes the specified command using the execution target's 
        ///     directory as a working directory. Returns the status.
        /// </summary>
        Task<int> ExecuteAsync(
            string command,
            IReadOnlyCollection<string> arguments,
            CancellationToken cancel);
    }
}
