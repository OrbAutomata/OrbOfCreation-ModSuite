using System;
using System.Collections.Generic;
using System.IO;

namespace OrbModding.IlInspect;

internal static class IlInspectApplication
{
    internal static int Run(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        Func<string?>? gameDirectoryEnvironment = null)
    {
        try
        {
            var command = CommandLine.Parse(
                args,
                gameDirectoryEnvironment ?? (() => Environment.GetEnvironmentVariable("OOC_GAME_DIR")));
            using var inspector = AssemblyInspector.Open(
                command.AssemblyPath, command.ManagedDirectory);
            inspector.WriteHeader(output);
            inspector.Execute(command.Verb, command.Query, output);
            return 0;
        }
        catch (CommandLineException exception)
        {
            error.WriteLine(exception.Message);
            error.WriteLine(CommandLine.Usage);
            return 2;
        }
        catch (Exception exception)
        {
            error.WriteLine($"IL inspection failed: {exception.Message}");
            return 1;
        }
    }
}
