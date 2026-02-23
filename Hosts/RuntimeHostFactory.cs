namespace HyPrism.Hosts;

public static class RuntimeHostFactory
{
    public static string CurrentRuntimeId { get; private set; } = "electron";

    public static IRuntimeHost Create(string[] args)
    {
        var runtime = ResolveRuntime(args);
        CurrentRuntimeId = runtime;

        return runtime switch
        {
            "tauri" => new TauriRuntimeHost(),
            _ => new ElectronRuntimeHost(),
        };
    }

    private static string ResolveRuntime(string[] args)
    {
        const string defaultRuntime = "electron";

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--runtime=", StringComparison.OrdinalIgnoreCase))
            {
                return arg["--runtime=".Length..].Trim().ToLowerInvariant();
            }

            if (string.Equals(arg, "--runtime", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1].Trim().ToLowerInvariant();
                }
            }
        }

        return defaultRuntime;
    }
}
