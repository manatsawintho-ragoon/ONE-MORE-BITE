namespace KruaImDee.Services;

public static class FileStorage
{
    public static string DataFolder(IWebHostEnvironment environment)
    {
        string? folder = Environment.GetEnvironmentVariable("ONE_MORE_BITE_DATA");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            return Path.GetFullPath(folder);
        }
        return Path.Combine(environment.ContentRootPath, "App_Data");
    }

    public static string UploadFolder(IWebHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ONE_MORE_BITE_DATA")))
        {
            return Path.Combine(DataFolder(environment), "uploads");
        }
        return Path.Combine(environment.WebRootPath, "uploads");
    }
}
