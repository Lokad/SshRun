using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using System.Collections.Generic;
using System.Text;

namespace SshRun
{
    public sealed class SshExecutionTarget : IExecutionTarget
    {
        /// <inheritdoc cref="IExecutionTarget.RootPath"/>
        public string RootPath { get; }

        /// <summary>
        ///     All sub-directories of the <see cref="RootPath"/> that have been created
        ///     in order to upload files. 
        /// </summary>
        /// <remarks>
        ///     Contains the absolute paths of the created subdirectories (this includes
        ///     the directory itself).
        ///     
        ///     This field is used to avoid having to create 
        /// </remarks>
        private readonly HashSet<string> _createdSubdirectories = new();

        /// <summary>
        ///     Used to create <see cref="_sshClient"/> and <see cref="_scpClient"/>,
        ///     if they are not already created (or provided by the constructor).
        ///     Can be null if the clients are already available.
        /// </summary>
        private readonly ConnectionInfo? _connectionInfo;

        /// <summary> Create a new target for executing SSH. </summary>
        /// <param name="connectionInfo"> Used to create SSH and SCP connections. </param>
        /// <param name="rootPath">
        ///     The absolute path of the root directory, on the remote server, where all the 
        ///     data related to the execution will be uploaded. When disposed, this object
        ///     will delete this directory and all its contents. 
        /// </param>
        public SshExecutionTarget(
            ConnectionInfo connectionInfo,
            string rootPath)
        {
            RootPath = rootPath;
            _connectionInfo = connectionInfo;
        }

        internal SshExecutionTarget(
            ScpClient? scpClient,
            SshClient? sshClient,
            string rootPath)
        {
            RootPath = rootPath;
            _scpClient = scpClient;
            _sshClient = sshClient;
        }

        /// <summary>
        ///     The SCP client used to copy files to and from the remote.
        /// </summary>
        private ScpClient? _scpClient = null;

        /// <summary>
        ///     The SSH client used to execute commands on the remote.
        /// </summary>
        private SshClient? _sshClient = null;

        private ScpClient ScpClient
        {
            get
            {
                if (_scpClient == null)
                {
                    _scpClient = new ScpClient(_connectionInfo);
                    _scpClient.Connect();
                }

                return _scpClient;
            }
        }

        private SshClient SshClient
        {
            get
            {
                if (_sshClient == null)
                {
                    _sshClient = new SshClient(_connectionInfo);
                    _sshClient.Connect();
                }

                return _sshClient;
            }
        }

        public void Dispose()
        {
            try
            {
                SshClient.RunCommand("rm -rf " + RemotePathTransformation.ShellQuote.Transform(RootPath));
            }
            catch { }

            _scpClient?.Disconnect();
            _sshClient?.Disconnect();
        }

        /// <inheritdoc cref="IExecutionTarget.ExecuteAsync(string, IReadOnlyCollection{string}, bool, CancellationToken)"/>
        public Task<int> ExecuteAsync(
            string command, 
            IReadOnlyCollection<string> arguments, 
            bool sudo, 
            CancellationToken cancel)
        {
            var quote = RemotePathTransformation.ShellQuote;

            var fullCommand = new StringBuilder();
            fullCommand.Append("cd ");
            fullCommand.Append(quote.Transform(RootPath));
            fullCommand.Append(" ; ");

            if (sudo)
                fullCommand.Append("sudo ");

            fullCommand.Append(quote.Transform(command));
            foreach (var argument in arguments)
            {
                fullCommand.Append(' ');
                fullCommand.Append(quote.Transform(argument));
            }

            var sshCommand = SshClient.CreateCommand(fullCommand.ToString());

            var tcs = new TaskCompletionSource<int>();

            var registration = cancel.Register(() =>
            {
                sshCommand.CancelAsync();
                tcs.TrySetCanceled(cancel);
            });

            tcs.Task.ContinueWith(_ => registration.Dispose(), cancel);

            sshCommand.BeginExecute(asyncResult =>
            {
                try
                {
                    sshCommand.EndExecute(asyncResult);
                    tcs.TrySetResult(sshCommand.ExitStatus);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            });
            
            return tcs.Task;
        }

        /// <inheritdoc cref="IExecutionTarget.WriteFileAsync(byte[], string, CancellationToken)"/>
        public Task WriteFileAsync(
            byte[] fileContents, 
            RemoteFile file, 
            CancellationToken cancel)
        =>
            WriteFileAsync(new MemoryStream(fileContents), file, cancel);

        /// <inheritdoc cref="IExecutionTarget.WriteFileAsync(Stream, RemoteFile, CancellationToken)"/>
        public Task WriteFileAsync(Stream fileContents, RemoteFile file, CancellationToken cancel)
        {
            EnsureParentDirectory(file);
            ScpClient.Upload(fileContents, file.Path);
            return Task.CompletedTask;
        }

        /// <summary> 
        ///     The path of the directory containing the specific path,
        ///     or "/" if the path is already "/". The returned path does
        ///     not end with "/" unless it is already "/". The input path is 
        ///     assumed to be absolute (and so, start with '/').
        /// </summary>
        private static string DirName(string path)
        {
            if (path == "/") return path;

            var last = path.LastIndexOf('/');
            var dirname = path.Substring(0, last);

            return dirname == "" ? "/" : dirname;
        }

        /// <summary>
        ///     Create the directory that will contain the provided remote file,
        ///     if it does not already exist.
        /// </summary>
        private void EnsureParentDirectory(RemoteFile file)
        {
            var dir = DirName(file.Path);
            if (!_createdSubdirectories.Contains(dir))
            {
                SshClient.RunCommand("mkdir -p " + RemotePathTransformation.ShellQuote.Transform(dir));
                _createdSubdirectories.Add(dir);
                while (dir != "/" && dir != RootPath)
                {
                    dir = DirName(dir);
                    _createdSubdirectories.Add(dir);
                }                
            }
        }

        /// <inheritdoc cref="IExecutionTarget.WriteFileAsync(FileInfo, RemoteFile, CancellationToken)"/>
        public Task WriteFileAsync(FileInfo local, RemoteFile file, CancellationToken cancel)
        {
            EnsureParentDirectory(file);
            ScpClient.Upload(local, file.Path);
            return Task.CompletedTask;
        }

        /// <inheritdoc cref="IExecutionTarget.ReadFileAsync(RemoteFile, Stream, CancellationToken)"/>
        public Task ReadFileAsync(RemoteFile file, Stream local, CancellationToken cancel)
        {
            ScpClient.Download(file.Path, local);
            return Task.CompletedTask;
        }
    }
}
