using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SshRun
{
    /// <summary>
    ///     Represents the contents of a manifest file: for every path, the hash
    ///     of the file at that path.
    /// </summary>
    /// <remarks>
    ///     Manifest files are used to avoid re-uploading files already present on the 
    ///     remote server, by checking their hashes locally and comparing them to the
    ///     remote hashes.
    /// </remarks>
    public sealed class Manifest
    {
        /// <summary>
        ///     For every remote path, a hash encoded as a hex string.
        /// </summary>
        private readonly Dictionary<string, string> _hashByPath = new();
        
        /// <summary>
        ///     Given a local file, associate its hash to the provided path, and
        ///     return that hash.
        /// </summary>
        public string AddHashFor(string path, FileInfo file)
        {
            if (path.Contains('\n')) 
                throw new ArgumentException("Path may not contain '\\n'.", nameof(path));

            using var sha = new SHA1Managed();
            using var data = file.OpenRead();

            var hash = sha.ComputeHash(data);

            var sb = new StringBuilder();
            foreach (var b in hash) sb.Append(b.ToString("X2"));

            var hashstr = sb.ToString();

            return _hashByPath[path] = hashstr;
        }

        /// <summary>
        ///     True if the file with the specified path is present in the manifest,
        ///     and has the specified hash.
        /// </summary>
        public bool ExistsWithHash(string path, string hash) =>
            _hashByPath.TryGetValue(path, out var oldhash) &&
            oldhash.Equals(hash, StringComparison.OrdinalIgnoreCase);

        /// <summary> 
        ///     Serialize the contents of the manifest to a string from which they 
        ///     can be parsed back. 
        /// </summary>
        public string SerializeToString()
        {
            var sb = new StringBuilder();
            foreach (var kv in _hashByPath)
                sb.Append(kv.Value)
                  .Append(' ')
                  .Append(kv.Key)
                  .Append('\n');
            return sb.ToString();
        }

        /// <summary>
        ///     Deserialize a string produced by <see cref="SerializeToString"/>.
        /// </summary>
        public static bool TryDeserializeFromString(
            string manifest, 
            [NotNullWhen(true)] out Manifest? result)
        {
            result = new Manifest();

            var i = 0;
            while (i < manifest.Length)
            {
                var nextSpace = manifest.IndexOf(' ', i);
                if (nextSpace < 0)
                {
                    result = null;
                    return false;
                }

                var nextEndOfLine = manifest.IndexOf('\n', nextSpace);
                if (nextEndOfLine < 0)
                {
                    result = null;
                    return false;
                }

                var hash = manifest[i..nextSpace];
                var path = manifest[(nextSpace+1)..nextEndOfLine];
                i = nextEndOfLine + 1;

                if (!result._hashByPath.TryAdd(path, hash))
                {
                    result = null;
                    return false;
                }
            }

            return true;
        }
    }
}
