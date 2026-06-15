using DotBoxD.Kernels.Example.Hosting.Examples;

Console.WriteLine("Safe IR hosting examples");

await ValueBindingExample.RunAsync();
await ContextBindingExample.RunAsync();
await RuntimeConfigurationExample.RunAsync();
await ExecutionModeExample.RunAsync();
