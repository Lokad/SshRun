using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using SshRun.Contracts;

namespace SshRun
{
    public static class Program
    {
        /// <summary>
        ///     The absolute path of the '.sshrun' folder created by the invocation 
        ///     function. Only available if the current processes was executed as 
        ///     the runner itself (i.e. on the remote server). 
        /// </summary>
        internal static string? SshRunPath { get; private set; }

        /// <summary> Invokes the specified command. </summary>
        /// <param name="args">
        ///     The first cell is the path to the command file, which will be
        ///     parsed as a <see cref="Command"/> and then invoked. 
        ///     
        ///     The second cell is an optional path to an output file. If 
        ///     present, and the command returns a non-void, non-Task value,
        ///     then that value is serialized to JSON and saved to the output file.
        /// </param>
        internal static async Task Main(string[] args)
        {
            SshRunPath = GetSshRunPath(args);

            Command command;
            using (var file = OpenCommandFile($"{SshRunPath}/command"))
                command = ParseCommandFile(file);

            await RunCommand(command, $"{SshRunPath}/result");
        }

        private static async Task RunCommand(Command command, string? outputPath)
        {
            var type = GetType(command.AssemblyFile, command.TypeName, "command");

            var method = GetStaticMethod(type, command.MethodName);

            var arguments = command.Arguments.Select(LoadArgument).ToArray();

            var result = method.Invoke(null, arguments);
            var resultType = method.ReturnType;

            // If the result is a task, wait for it to complete before deciding what
            // to do with it.
            if (result is Task t) await t;

            if (resultType == typeof(void) || outputPath == null)
                return; // Nothing to write, or no request to write

            // Unwrap task.
            if (resultType.IsGenericType && 
                resultType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                result = result == null ? null : TaskResult((dynamic)result);
                resultType = resultType.GetGenericArguments()[0];
            }

            var json = JsonSerializer.Serialize(result, resultType);
            File.WriteAllText(outputPath, json);
        }

        private static object? TaskResult<T>(Task<T> result) => 
            result.GetAwaiter().GetResult();

        private static object? LoadArgument(Argument argument) =>
            JsonSerializer.Deserialize(
                argument.Json,
                GetType(argument.AssemblyFile, argument.TypeName, "argument"));

        private static Type GetType(string? assemblyFile, string typeName, string kind)
        {
            if (assemblyFile == null)
            {
                try
                {
                    return Type.GetType(typeName)
                        ?? throw new ArgumentException(
                            $"{kind} type not found: {typeName}",
                            nameof(typeName));
                }
                catch
                {
                    Console.WriteLine(
                        "ERROR: SshRun: cannot load {0} type '{1}'.",
                        kind, typeName);
                    throw;
                }
            }
            else
            {
                Assembly assembly;
                try
                {
                    assembly = Assembly.LoadFrom(assemblyFile);
                }
                catch
                {
                    Console.WriteLine(
                        "ERROR: SshRun: cannot load {0} assembly '{1}'.",
                        kind, assemblyFile);
                    throw;
                }

                try
                {
                    return assembly.GetType(typeName)
                        ?? throw new ArgumentException(
                            $"{kind} type not found: {typeName} in {assembly.FullName}",
                            nameof(typeName));
                }
                catch
                {
                    Console.WriteLine(
                        "ERROR: SshRun: cannot load {0} type '{1}' from {2}.",
                        kind, typeName, assembly.FullName);
                    throw;
                }
            }
        }

        private static MethodInfo GetStaticMethod(Type type, string methodName)
        {
            try
            {
                return type.GetMethod(methodName) ??
                    throw new ArgumentException(
                        $"Method not found: {methodName} in type {type.AssemblyQualifiedName}",
                        nameof(methodName));
            }
            catch
            {
                Console.WriteLine(
                    "ERROR: SshRun: cannot find method '{0}' in type {1}.",
                    methodName,
                    type.AssemblyQualifiedName);
                throw;
            }
        }

        private static Command ParseCommandFile(FileStream file)
        {
            using var reader = new BinaryReader(file);

            try
            {
                return Command.Read(reader);
            }
            catch
            {
                Console.WriteLine("ERROR: SshRun.Runner: when parsing command file.");
                throw;
            }
        }

        private static FileStream OpenCommandFile(string filePath)
        { 
            try
            {
                return File.OpenRead(filePath);
            }
            catch
            {
                Console.WriteLine("ERROR: SshRun.Runner: could not open command file '{0}'.", filePath);
                throw;
            }
        }

        /// <summary>
        ///     The runner expects a single argument, which is the path of the .sshrun 
        ///     directory created by the invocation code.
        /// </summary>
        private static string GetSshRunPath(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("SshRun.Runner: please provide path of .sshrun directory");
                Environment.Exit(-1);
            }

            return args[0];
        }
    }
}
