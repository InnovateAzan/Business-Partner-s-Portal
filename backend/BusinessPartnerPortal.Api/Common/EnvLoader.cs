namespace BusinessPartnerPortal.Api.Common;

public static class EnvLoader
{
    public static void Load(string fileName = ".env")
    {
        var path = Path.Combine(
            Directory.GetCurrentDirectory(),
            fileName
        );

        if (!File.Exists(path))
        {
            return;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();

            if (
                string.IsNullOrWhiteSpace(line)
                ||
                line.StartsWith('#')
            )
            {
                continue;
            }

            var idx = line.IndexOf('=');

            if (idx <= 0)
            {
                continue;
            }

            var key =
                line[..idx]
                    .Trim();

            var value =
                line[(idx + 1)..]
                    .Trim()
                    .Trim('"');

            /*
             * IMPORTANT:
             *
             * The project's .env file is the configuration source
             * for this deployment.
             *
             * Always apply its value so an old Windows/process
             * environment variable (for example FRONTEND_PORT=8080)
             * cannot override the current .env value (8088).
             */
            Environment.SetEnvironmentVariable(
                key,
                value,
                EnvironmentVariableTarget.Process
            );
        }
    }
}