namespace AiEvalGate.EvaluationTests;

internal static class TestPaths
{
    public static string FindRepositoryRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "evals")) && Directory.Exists(Path.Combine(dir, "src")))
            {
                return dir;
            }

            DirectoryInfo? parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root. Expected directories: evals and src.");
    }
}
