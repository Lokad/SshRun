using SshRun.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SshRun
{
    public sealed class SshRunner : IDisposable
    {
        private readonly IExecutionTarget _target;

        private readonly CommandExtractor _extractor;

        /// <summary>
        ///     If false, the <see cref="_target"/> will be disposed along
        ///     with the instance of <see cref="SshRunner"/>/
        /// </summary>
        private readonly bool _leaveOpen;

        /// <summary>
        ///     Have the executable files and DLLs been copied to the remote, by 
        ///     calling <see cref="WriteExecFiles(CancellationToken)"/> ?
        /// </summary>
        private bool _hasCopiedExecFiles = false;

        /// <summary> Prefix commands with `sudo` to act as root ? </summary>
        public bool Sudo { get; set; }

        public SshRunner()
        {
            _target = new LocalTempExecutionTarget();
            _extractor = new CommandExtractor();
            _leaveOpen = false;
        }

        public SshRunner(IExecutionTarget target, bool leaveOpen = false)
        {
            _target = target;
            _extractor = new CommandExtractor();
            _leaveOpen = leaveOpen;
        }

        public void Dispose()
        {
            if (!_leaveOpen)
                _target.Dispose();
        }

        private Task WriteCommand(Command command, CancellationToken cancel)
        {
            var commandMemoryStream = new MemoryStream();
            using (var writer = new BinaryWriter(commandMemoryStream))
                command.Write(writer);

            return _target.WriteFileAsync(commandMemoryStream.ToArray(), CommandFilePath(), cancel);
        }

        private async Task WriteExecFiles(CancellationToken cancel)
        {
            if (_hasCopiedExecFiles) return;

            var sourceDir = AppDomain.CurrentDomain.BaseDirectory;
            var relativePaths = Directory.GetFiles(sourceDir).Select(path =>
                Path.GetFileName(path) ?? throw new ArgumentException($"Invalid file path {path}."));

            // The 'changesDetected' flag is set when a missing (or changed) file is identified, and 
            // thus uploads start. At that point, the remote manifest file is removed, and will
            // be restored afterwards. 
            var changesDetected = false;

            var oldManifest = await RetrieveManifestFileAsync(cancel);
            var newManifest = new Manifest();

            foreach (var relPath in relativePaths)
            {
                var remotePath = new RemoteFile($"{_target.RootPath}/{relPath}");
                var file = new FileInfo(Path.Combine(sourceDir, relPath));

                var newHash = newManifest.AddHashFor(remotePath.Path, file);
                if (oldManifest.ExistsWithHash(remotePath.Path, newHash)) continue;

                if (!changesDetected)
                {
                    await RemoveManifestFileAsync(cancel);
                    changesDetected = true;
                }

                await _target.WriteFileAsync(file, remotePath, cancel);
            }

            if (changesDetected)
                await WriteManifestFileAsync(newManifest, cancel);

            _hasCopiedExecFiles = true;
        }

        private async Task<int> DoRunAsync<T>(Expression<T> expr, CancellationToken cancel)
        {
            var command = _extractor.FromLinqExpression(expr);

            await WriteCommand(command, cancel);
            await WriteExecFiles(cancel);

            return await _target.ExecuteAsync("dotnet", new[] {
                "SshRun.dll",
                $"{_target.RootPath}/.sshrun"
            }, Sudo, cancel);
        }

        private RemoteFile CommandFilePath() =>
            new($"{_target.RootPath}/.sshrun/command");

        private RemoteFile ResultFilePath() =>
            new($"{_target.RootPath}/.sshrun/result");

        /// <summary>
        ///     The path of the manifest file in the remote directory. The file
        ///     contains an instance of <see cref="Manifest"/> representing the hashes
        ///     of the files currently present on the remote, which can be used to 
        ///     avoid re-uploading files that are already present there.
        /// </summary>
        private RemoteFile ManifestFilePath() =>
            new($"{_target.RootPath}/.sshrun/manifest");

        /// <summary> Download the manifest file present on the remote. </summary>
        /// <remarks> 
        ///     If there is no manifest file on the remote, then returns the
        ///     empty one. 
        /// </remarks>
        /// <see cref="ManifestFilePath"/>
        private async Task<Manifest> RetrieveManifestFileAsync(CancellationToken cancel)
        {
            var ms = new MemoryStream();
            try
            {
                await _target.ReadFileAsync(ManifestFilePath(), ms, cancel);
                var str = Encoding.UTF8.GetString(ms.ToArray());

                if (Manifest.TryDeserializeFromString(str, out var result))
                    return result;
            }
            catch { }

            return new Manifest();
        }

        /// <summary> Deletes the remote manifest file. </summary>
        /// <see cref="ManifestFilePath"/>
        private Task RemoveManifestFileAsync(CancellationToken cancel) =>
            _target.RemoveFileAsync(ManifestFilePath(), cancel);

        /// <summary> Overwrites or creates the remote manifest file. </summary>
        /// <see cref="ManifestFilePath"/>
        private async Task WriteManifestFileAsync(Manifest manifest, CancellationToken cancel)
        {
            try
            {
                var str = manifest.SerializeToString();
                await _target.WriteFileAsync(
                    Encoding.UTF8.GetBytes(str), 
                    ManifestFilePath(), 
                    cancel);
            }
            catch { }
        }

        public Task<int> RunAsync(Expression<Action> action, CancellationToken cancel) =>
            DoRunAsync(action, cancel);

        public async Task<T> RunAsync<T>(Expression<Func<T>> action, CancellationToken cancel)
        {
            var status = await DoRunAsync(action, cancel);

            if (status != 0)
                throw new SshRunnerException($"Process finished with status {status}");

            using var ms = new MemoryStream();
            await _target.ReadFileAsync(ResultFilePath(), ms, cancel);

            return JsonSerializer.Deserialize<T>(ms.ToArray()) ?? 
                throw new NullReferenceException("Null return value received");
        }

        public async Task<T> RunAsync<T>(Expression<Func<Task<T>>> action, CancellationToken cancel)
        {
            var status = await DoRunAsync(action, cancel);

            if (status != 0)
                throw new SshRunnerException($"Process finished with status {status}");

            using var ms = new MemoryStream();
            await _target.ReadFileAsync(ResultFilePath(), ms, cancel);

            return JsonSerializer.Deserialize<T>(ms.ToArray()) ??
                throw new NullReferenceException("Null return value received");
        }

        public async Task<RemoteFile> UploadAsync(byte[] bytes, CancellationToken cancel)
        {
            var path = $"{_target.RootPath}/.sshrun/{Guid.NewGuid()}";
            var file = new RemoteFile(path);
            await _target.WriteFileAsync(bytes, file, cancel);
            return file;
        }

        public async Task<RemoteFile> UploadAsync(Stream stream, CancellationToken cancel)
        {
            var path = $"{_target.RootPath}/.sshrun/{Guid.NewGuid()}";
            var file = new RemoteFile(path);
            await _target.WriteFileAsync(stream, file, cancel);
            return file;
        }

        public async Task<RemoteFile> UploadAsync(FileInfo local, CancellationToken cancel)
        {
            var path = $"{_target.RootPath}/.sshrun/{Guid.NewGuid()}";
            var file = new RemoteFile(path);
            await _target.WriteFileAsync(local, file, cancel);
            return file;
        }

        public async Task<byte[]> DownloadAsync(RemoteFile file, CancellationToken cancel)
        {
            var ms = new MemoryStream();
            await _target.ReadFileAsync(file, ms, cancel);
            return ms.ToArray();
        }

        public Task DownloadAsync(RemoteFile file, Stream dest, CancellationToken cancel) =>
            _target.ReadFileAsync(file, dest, cancel);

        public async Task DownloadAsync(RemoteFile file, FileInfo dest, CancellationToken cancel)
        {
            using var fs = dest.OpenWrite();
            await DownloadAsync(file, fs, cancel);
        }
    }
}
