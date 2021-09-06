using SshRun.Contracts;
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace SshRun
{
    internal sealed class CommandExtractor
    {
        public Command FromLinqExpression<T>(Expression<T> action)
        {
            if (action.Body is not MethodCallExpression call)
                throw new ArgumentException("Lambda should be a method call.");

            if (call.Object != null)
                throw new ArgumentException("Called method should be static.");

            var method = call.Method;
            var type = method.DeclaringType
                ?? throw new ArgumentException($"Method {method} has no declaring type");

            var arguments = call.Arguments.Select((argument, i) =>
            {
                var type = argument.Type;
                var assemblyFile = type.Assembly.GetName().Name == "System" ? null :
                                        Path.GetFileName(type.Assembly.Location);

                if (argument is ConstantExpression constexpr)
                {
                    return new Argument(
                        AssemblyFile: assemblyFile,
                        TypeName: type.FullName!,
                        Json: JsonSerializer.Serialize(constexpr.Value, type));
                }

                if (argument is MemberExpression memexpr &&
                    memexpr.Expression is ConstantExpression objexpr &&
                    memexpr.Member is FieldInfo fi)
                {
                    var value = fi.GetValue(objexpr.Value);
                    return new Argument(
                        AssemblyFile: assemblyFile,
                        TypeName: type.FullName!,
                        Json: JsonSerializer.Serialize(value, type));
                }

                throw new ArgumentException(
                    $"Argument {i} is not a constant or a local variable.",
                    nameof(argument));

            }).ToArray();

            return new Command(
                AssemblyFile: Path.GetFileName(type.Assembly.Location),
                TypeName: type.FullName!,
                MethodName: method.Name,
                Arguments: arguments);
        }
    }
}
