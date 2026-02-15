using System.Collections.Generic;

namespace HyPrism.Models;

// CurseForge API response models
public class CurseForgeSearchResponse
{
    public List<CurseForgeMod>? Data { get; set; }
    public CurseForgePagination? Pagination { get; set; }
}

public class CurseForgeModResponse
{
    public CurseForgeMod? Data { get; set; }
}

public class CurseForgePagination
{
    public int Index { get; set; }
    public int PageSize { get; set; }
    public int ResultCount { get; set; }
    public int TotalCount { get; set; }
}

public class CurseForgeMod
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Summary { get; set; }
    public int DownloadCount { get; set; }
    public string? DateCreated { get; set; }
    public string? DateModified { get; set; }
    public CurseForgeLogo? Logo { get; set; }
    public List<CurseForgeCategory>? Categories { get; set; }
    public List<CurseForgeAuthor>? Authors { get; set; }
    public List<CurseForgeFile>? LatestFiles { get; set; }
    public List<CurseForgeScreenshot>? Screenshots { get; set; }
}

public class CurseForgeScreenshot
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Url { get; set; }
}

public class CurseForgeLogo
{
    public int Id { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Url { get; set; }
}

public class CurseForgeCategory
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public int ParentCategoryId { get; set; }
    public bool? IsClass { get; set; }
}

public class CurseForgeAuthor
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
}

public class CurseForgeFile
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public string? DisplayName { get; set; }
    public string? FileName { get; set; }
    public string? DownloadUrl { get; set; }
    public long FileLength { get; set; }
    public string? FileDate { get; set; }
    public int ReleaseType { get; set; }
    public int DownloadCount { get; set; }
    public List<string>? GameVersions { get; set; }
}

public class CurseForgeCategoriesResponse
{
    public List<CurseForgeCategory>? Data { get; set; }
}

public class CurseForgeFilesResponse
{
    public List<CurseForgeFile>? Data { get; set; }
    public CurseForgePagination? Pagination { get; set; }
}

public class CurseForgeFileResponse
{
    public CurseForgeFile? Data { get; set; }
}

public class CurseForgeDownloadUrlResponse
{
    public string? Data { get; set; }
}
