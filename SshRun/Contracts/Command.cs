using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SshRun.Contracts
{
    /// <summary> 
    ///     Binary-serializable data contract, describes the static method that should
    ///     be invoked by the runner.
    /// </summary>
    /// <remarks>
    ///     Made available to SshRun.dll so that it can generate the remote invocation.
    /// </remarks>
    /// <param name="AssemblyFile"> 
    ///     The name of the assembly file containing the function to execute. 
    /// </param>
    /// <param name="TypeName"> 
    ///     The name of the type containing the method to execute. 
    /// </param>
    /// <param name="MethodName">
    ///     The name of the static method to invoke.
    /// </param>
    /// <param name="Arguments"> 
    ///     The arguments passed to the method. 
    /// </param>
    internal sealed record Command(
        string AssemblyFile,
        string TypeName,
        string MethodName,
        Argument[] Arguments)
    {
        internal static Command Read(BinaryReader reader) => new(
            AssemblyFile: reader.ReadString(),
            TypeName:     reader.ReadString(),
            MethodName:   reader.ReadString(),
            Arguments:    Enumerable.Range(0, reader.ReadInt32())
                            .Select(_ => Argument.Read(reader))
                            .ToArray());

        internal void Write(BinaryWriter writer)
        {
            writer.Write(AssemblyFile);
            writer.Write(TypeName);
            writer.Write(MethodName);
            writer.Write(Arguments.Length);
            foreach (var arg in Arguments)
                arg.Write(writer);
        }
    }
}
