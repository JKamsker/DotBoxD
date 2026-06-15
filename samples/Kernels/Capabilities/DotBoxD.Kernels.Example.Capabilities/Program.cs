using DotBoxD.Kernels.Example.Capabilities.Examples;

Console.WriteLine("Safe IR capabilities examples");

await CustomBindingExample.RunAsync();
await SafeLoggingExample.RunAsync();
await ResourceLimitsExample.RunAsync();
await AuditObserverExample.RunAsync();
