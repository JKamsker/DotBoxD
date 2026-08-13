using DotBoxD.Kernels.Debugging.Clr;

return args is ["--dotboxd-debug-worker"]
    ? await ClrDebugWorkerProgram.RunAsync().ConfigureAwait(false)
    : 2;
