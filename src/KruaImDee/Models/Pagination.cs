namespace KruaImDee.Models;

public class Pagination
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }

    public bool HasPrevious
    {
        get { return Page > 1; }
    }

    public bool HasNext
    {
        get { return Page < TotalPages; }
    }

    public Pagination(int page, int pageSize, int totalCount)
    {
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));
        Page = Math.Clamp(page, 1, TotalPages);
    }
}
