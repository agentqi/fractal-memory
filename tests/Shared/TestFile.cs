internal static class TestFile
{
    public static Task<string> ReadAllTextAsync(string path) =>
        File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

    public static Task WriteAllTextAsync(string path, string contents) =>
        File.WriteAllTextAsync(path, contents, TestContext.Current.CancellationToken);
}
