using DotBoxD.Fuzzing.Targets;
using SharpFuzz;

if (args is ["--write-corpus", var directory])
{
    SeedCorpus.Write(directory);
    return;
}

if (args.Length == 0 || !FuzzTargets.IsKnown(args[0]) ||
    (args.Length != 1 && (args.Length != 3 || args[1] != "--replay")))
{
    Console.Error.WriteLine("Usage: DotBoxD.Fuzzing <json|verifier|framing|messagepack-request|messagepack-response> [--replay <file-or-directory>]");
    Console.Error.WriteLine("       DotBoxD.Fuzzing --write-corpus <directory>");
    Environment.ExitCode = 2;
    return;
}

var target = args[0];
if (args.Length == 3)
{
    var files = Directory.Exists(args[2])
        ? Directory.GetFiles(args[2]).Order(StringComparer.Ordinal).ToArray()
        : [args[2]];
    if (files.Length == 0)
    {
        throw new InvalidOperationException("The replay corpus is empty.");
    }

    foreach (var file in files)
    {
        Console.WriteLine($"Replaying {target}: {file}");
        FuzzTargets.Run(target, File.ReadAllBytes(file));
    }

    return;
}

Fuzzer.OutOfProcess.Run(stream =>
{
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    FuzzTargets.Run(target, buffer.ToArray());
});
