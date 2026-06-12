using CodeEnforcer;
using Spectre.Console.Cli;

CommandApp<CodeEnforcerCommand> app = new();
app.Configure(configuration => configuration.SetApplicationName("CodeEnforcer"));
return app.Run(args);
