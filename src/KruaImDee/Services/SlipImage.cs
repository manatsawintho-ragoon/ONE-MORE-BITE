namespace KruaImDee.Services;

public static class SlipImage
{
    public static string Extension(byte[] bytes)
    {
        if (bytes.Length < 12)
        {
            return "";
        }

        if (bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255)
        {
            return ".jpg";
        }

        byte[] png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Take(8).SequenceEqual(png))
        {
            return ".png";
        }

        if (
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF"
            && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP"
        )
        {
            return ".webp";
        }

        return "";
    }

    public static string ContentType(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();
        if (extension == ".jpg")
        {
            return "image/jpeg";
        }

        if (extension == ".png")
        {
            return "image/png";
        }

        if (extension == ".webp")
        {
            return "image/webp";
        }

        return "application/octet-stream";
    }
}
