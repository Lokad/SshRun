using System;
using System.Runtime.Serialization;

namespace SshRun
{
    [Serializable]
    internal class SshRunnerException : Exception
    {
        public SshRunnerException()
        {
        }

        public SshRunnerException(string? message) : base(message)
        {
        }

        public SshRunnerException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected SshRunnerException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}