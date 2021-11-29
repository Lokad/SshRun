using Renci.SshNet;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SshRun.Sample
{
    public sealed class Program
    {
        public record ToUpperRequest(string? Text, RemoteFile? File);

        static async Task Main(string[] args)
        {
            var connection = new ConnectionInfo(
                "51.103.26.20",
                "azureuser",
                new PrivateKeyAuthenticationMethod(
                    "azureuser",
                    new PrivateKeyFile(@"C:\Users\VictorNicollet\.ssh\id_rsa")
                ));

            var sshTarget = new SshExecutionTarget(connection, "/home/azureuser/test")
            {
                DeleteOnClose = false
            };

            using (var runner = new SshRunner(sshTarget))
            {
                var bytes = Encoding.UTF8.GetBytes(@"
Lorem ipsum dolor sit amet, consectetur adipiscing elit, 
sed do eiusmod tempor incididunt ut labore et dolore magna 
aliqua. Ut enim ad minim veniam, quis nostrud exercitation 
ullamco laboris nisi ut aliquip ex ea commodo consequat. 
Duis aute irure dolor in reprehenderit in voluptate velit 
esse cillum dolore eu fugiat nulla pariatur. Excepteur sint 
occaecat cupidatat non proident, sunt in culpa qui officia 
deserunt mollit anim id est laborum.");

                var messages = new[]
                {
                    new ToUpperRequest(null, await runner.UploadAsync(bytes, default)),
                    new ToUpperRequest("Zorkmids", null)
                };

                foreach (var message in messages)
                {
                    var sw = Stopwatch.StartNew();
                    var result = await runner.RunAsync(() => ToUpper(message), default);
                    if (result.Text != null) 
                        Console.WriteLine("Text: {0}", result.Text);

                    if (result.File != null)
                    {
                        var rbytes = await runner.DownloadAsync(result.File, default);
                        Console.WriteLine("File: {0}", Encoding.UTF8.GetString(rbytes));
                    }

                    Console.WriteLine("In {0}", sw.Elapsed);
                }
            }
        }

        public static ToUpperRequest ToUpper(ToUpperRequest msg)
        {
            if (msg.Text != null)
                return new(msg.Text.ToUpperInvariant(), null);

            if (msg.File != null)
            {
                using var text = msg.File.File.OpenText();
                var full = text.ReadToEnd();

                var result = SshRemote.GetFreshFile();
                using (var file = result.File.OpenWrite())
                using (var writer = new StreamWriter(file))
                    writer.Write(full.ToUpperInvariant());

                return new(null, result);
            }

            return new(null, null);
        }
    }
}
