using System.IO;
using System.Text.Json.Serialization;

namespace SshRun
{
    /// <summary> A file that was pushed from the initiator to the remote. </summary>
    /// <remarks>
    ///     This class is intended to be used as the type of arguments passed
    ///     to the remote-invoked static functions.
    ///     
    ///     The methods of this class are only intended to be executed on the remote 
    ///     server. On the initiator side, reading or writing these files needs to be 
    ///     done through the <code>SshRunner</code> class.
    /// </remarks>
    [JsonConverter(typeof(RemoteFileConverter))]
    public sealed class RemoteFile
    {
        public RemoteFile(string remotePath)
        {
            Path = remotePath;
        }

        /// <summary>
        ///     The file itself ; can only be accessed from within the remote function,
        ///     and not from initiator code.
        /// </summary>
        public FileInfo File => new(Path);

        /// <summary>
        ///     The file, opened as a read-only stream ; can only be accessed from 
        ///     within the remote function, and not from initiator code.
        /// </summary>
        public FileStream Open() => File.OpenRead();

        /// <summary>
        ///     The absolute path where this file is available on the remote server. 
        ///     This value is available both from the remote function and from 
        ///     the initiator code.
        /// </summary>
        public string Path { get; }
    }
}
