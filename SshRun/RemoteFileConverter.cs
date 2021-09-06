using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SshRun
{
    /// <summary>
    ///     JSON serialization and deserialization for remote files
    ///     (these are really only wrappers around a path on the remote server,
    ///     so they are serialized as strings). 
    /// </summary>
    internal class RemoteFileConverter : JsonConverter<RemoteFile>
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert == typeof(RemoteFile);

        public override RemoteFile? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        =>
            new(reader.GetString() ?? throw new NullReferenceException("Expected a string"));

        public override void Write(
            Utf8JsonWriter writer,
            RemoteFile value,
            JsonSerializerOptions options)
        =>
            writer.WriteStringValue(value.Path);
    }
}
