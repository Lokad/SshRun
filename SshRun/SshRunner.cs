using SshRun.Contracts;
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
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

            foreach (var relPath in relativePaths)
            {
                var remotePath = new RemoteFile($"{_target.RootPath}/{relPath}");
                var file = new FileInfo(Path.Combine(sourceDir, relPath));
                await _target.WriteFileAsync(file, remotePath, cancel);
            }

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
