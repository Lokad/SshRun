using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SshRun
{
    /// <summary>
    ///     An execution target that runs functions on the same machine, 
    ///     after copying data to the specified directory. Mostly intended
    ///     for testing the library.
    /// </summary>
    public sealed class LocalTempExecutionTarget : IExecutionTarget
    {
        /// <inheritdoc/>
        public string RootPath { get; } = 
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public LocalTempExecutionTarget()
        {
            Directory.CreateDirectory(RootPath);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch {}
        }

        /// <inheritdoc cref="IExecutionTarget.ExecuteAsync(string, IReadOnlyCollection{string}, bool, CancellationToken)"/>
        public async Task<int> ExecuteAsync(
            string command, 
            IReadOnlyCollection<string> arguments, 
            bool sudo,
            CancellationToken cancel)
        {
            if (sudo)
                throw new NotSupportedException("Local execution does not support 'sudo'");

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = RootPath,
                FileName = command
            };

            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);

            var process = Process.Start(psi) 
                ?? throw new NullReferenceException("Process.Start returned null");

            await process.WaitForExitAsync(cancel);

            return process.ExitCode;
        }

        /// <inheritdoc cref="IExecutionTarget.WriteFileAsync(byte[], string, CancellationToken)"/>
        public Task WriteFileAsync(
            byte[] fileContents,
            RemoteFile path,
            CancellationToken cancel)
        =>
            File.WriteAllBytesAsync(path.Path, fileContents, cancel);

        /// <inheritdoc cref="IExecutionTarget.WriteFileAsync(FileInfo, string, CancellationToken)"/>
        public Task WriteFileAsync(
            FileInfo file,
            RemoteFile path,
            CancellationToken cancel)
        {
            file.CopyTo(path.Path);
            return Task.CompletedTask;
        }

        /// <inheritdoc cref="IExecutionTarget.WriteFileAsync(Stream, RemoteFile, CancellationToken)"/>
        public async Task WriteFileAsync(
            Stream fileContents,
            RemoteFile path,
            CancellationToken cancel)
        {
            using var file = path.File.Open(FileMode.Create);
            await fileContents.CopyToAsync(file, cancel);
        }

        /// <inheritdoc cref="IExecutionTarget.ReadFileAsync(RemoteFile, Stream, CancellationToken)"/>
        public async Task ReadFileAsync(RemoteFile file, Stream local, CancellationToken cancel)
        {
            using var stream = file.Open();
            await stream.CopyToAsync(local, cancel);
        }
    }
}
