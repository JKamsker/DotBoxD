namespace SafeIR.AddendumExamples;

internal static class AddendumExampleRunner
{
    public static async Task RunAsync()
    {
        Console.WriteLine("Safe IR addendum examples");

        SimpleContractExamples.Run();
        await ValueBindingExample.RunAsync();
        await ContextBindingExample.RunAsync();
        await KernelClassExample.RunAsync();
        ManifestInspectionExample.Run();
        await JsonUploadExample.RunAsync();
        await RuntimeConfigurationExample.RunAsync();
        await HookSubscriptionExample.RunAsync();
        await ExecutionModeExample.RunAsync();
        await DesignGuidanceExample.RunAsync();
        InvalidToolingExamples.Describe();
    }
}
