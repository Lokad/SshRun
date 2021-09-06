using System.IO;

namespace SshRun.Contracts
{
    /// <summary>
    ///     Binary-serializable data contract, included in a <see cref="Command"/>
    ///     to store an argument passed to the static method invoked by 
    ///     the runner.
    /// </summary>
    /// <remarks>
    ///     Made available to SshRun.dll so that it can generate the remote invocation.
    ///     
    ///     Why don't we provide only the JSON string, and rely on the signature
    ///     of the command's method to deduce the type ? Because the actual type of 
    ///     the argument might be different from the signature's argument type
    ///     (for instance, a concrete class versus an interface).
    /// </remarks>
    /// <param name="AssemblyFile">
    ///     If provided, look for the <see cref="TypeName"/> in this file.
    /// </param>
    /// <param name="TypeName">
    ///     The name of the type to use for deserializing this argument.
    /// </param>
    /// <param name="Json">
    ///     The value of the argument, serialized as JSON. 
    /// </param>
    internal sealed record Argument(
        string? AssemblyFile,
        string TypeName,
        string Json)
    {
        internal static Argument Read(BinaryReader reader) => new(
            AssemblyFile: reader.ReadBoolean() ? reader.ReadString() : null,
            TypeName: reader.ReadString(),
            Json: reader.ReadString());

        internal void Write(BinaryWriter writer)
        {
            writer.Write(AssemblyFile != null);
            if (AssemblyFile != null) writer.Write(AssemblyFile);
            writer.Write(TypeName);
            writer.Write(Json);
        }
    }
}