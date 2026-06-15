using AgentQueue;
using AgentQueue.Core;

return new AgentQueueApp(Console.Out, Console.Error, SystemClock.Instance)
    .Run(args, Environment.CurrentDirectory);
