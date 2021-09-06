A convenient library to run .NET code remotely over SSH.

```csharp
// Example: use a remote machine to convert text to uppercase
async Task<string> RemoteToUppercase(string text, CancellationToken cancel)
{
    // Configure the connection using SSH.NET types
    var connection = new ConnectionInfo("example.com", "alice", 
        new PrivateKeyAuthenticationMethod("alice", privateKeyFile));

    // Specify remote directory where code will run (deleted on dispose)
    using var target = new SshExecutionTarget(connection, "/home/alice/remote");

    // Invoke 'ToUppercase(text)' on the remote server. 
    return await new SshRunner(target).RunAsync(() => ToUppercase(text), cancel);
}

// The public static function that will be executed on the remote target,
// with the argument provided in the callback passed above. 
public static string ToUppercase(string text) =>
    test.ToUpperInvariant();
```

This will perform the following steps: 

 1. Connect as `alice@example.com` over SSH and create directory `/home/alice/remote`. 
 2. Identify all the assemblies used by the current process, and upload them to `/home/alice/remote`.
 3. Serialize and upload the arguments of the invoked function.
 4. Start a new process on the remote server to load the assemblies, deserialize the arguments, invoke `ToUppercase`, and serialize the results.
 5. Download and deserialize the results of the remote execution.
 6. Delete `/home/alice/remote` and all its contents.

## Installation 

On the local machine, include the NuGet package `SshRun` in the application that needs to perform the remote invocation. 

On the remote machine, the following commands will be executed: 

 - The remote directory will be created with `mkdir -p` and removed with `rm -r`, so the remote user should have the permission to perform these, and to read and write files in that remote directory.
 - `dotnet run` will be invoked on the `SshRun.dll` assembly, so the correct version of `dotnet` should be installed on the machine. See [the Microsoft installation docs](https://docs.microsoft.com/en-us/dotnet/core/install/linux) for more details. 

## Constraints

All assemblies necessary to execute the remote function should be present in `AppDomain.CurrentDomain.BaseDirectory`. The contents of this directory will be copied over to the remote machine.

The callback passed to `SshRunner.RunAsync` must be of the form:

```csharp
() => PublicClass.PublicStaticMethod(localVars)
```

That is, the invoked method should be public and static, and a member of a public class, and the callback may not contain any operations other than a call to this method. The arguments must all be local variables in the containing function. 

The arguments must be of types that can be serialized to JSON ; the serialization will be performed with `System.Text.Json`. 

The return type of the method can be `void` or `Task`, or it can be `T` or `Task<T>` where `T` is a type that can be serialized to JSON (again, with `System.Text.Json`). In the former case, `RunAsync` returns a `Task` ; in the latter case, it returns a `Task<T>`. 
 
## File Transfer

Files present on the remote machine are represented with type `SshRun.Runner.RemoteFile`. The available methods and properties depend on whether the code is running on the remote machine. 

Consider a variant of the `RemoteToUppercase` method that can convert either a string or an entire file to JSON: 

```csharp
// JSON-serializable payload, either a string or a remote file reference
record Payload(string? Text, RemoteFile? File);

// The method that will be executed on the remote machine
public static Payload ToUppercase(Payload input)
{
    if (input.Text is string txt) 
        return Payload(txt.ToUpperInvariant(), null);

    if (input.File is RemoteFile rf)
    {
        // This code is running on the remote machine, and so the remote file's
        // path can be accessed. 
        var full = File.ReadAllText(rf.Path);

        // Create a new local file (will be deleted along with the rest once
        // the SshExecutionTarget is disposed).
        var result = SshRemote.GetFreshFile();
        File.WriteAllText(result.Path, full.ToUpperInvariant());

        return Payload(null, result);
    }

    throw new ArgumentException("Empty payload");
}

// Invoke the remote method to overwrite a local file with its 
// uppercase equivalent.
public static async Task RemoteToUppercase(
    FileInfo localFile, SshRunner runner, CancellationToken cancel) 
{
    // Upload the local file to the remote, creating a RemoteFile to 
    // represent it locally.
    var remoteFile = await runner.UploadAsync(localFile, cancel);

    // Invoke the remote method. 
    var payload = Payload(null, remoteFile);
    var result = await runner.RunAsync(() => ToUppercase(payload), cancel);

    // The result contains a reference to a remote file, download it to 
    // overwrite the local file.
    await runner.DownloadAsync(result.File, localFile, cancel);
}
```

It is also possible to create a `RemoteFile` from any absolute path on the remote server.

```csharp
var rf = new RemoteFile("/home/alice/example.txt");
```
