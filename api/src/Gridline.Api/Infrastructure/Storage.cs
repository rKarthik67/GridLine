namespace Gridline.Infrastructure;

/// <summary>Files on disk (uploads, imports, proposals). Root comes from Storage:Root (default ./data).</summary>
public sealed class Storage(IConfiguration cfg, IWebHostEnvironment env)
{
    public string Root { get; } = Path.GetFullPath(cfg["Storage:Root"] is { Length: > 0 } r ? r : Path.Combine(env.ContentRootPath, "data"));

    public string PathFor(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw ApiException.Bad("Bad file path.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    public async Task<(string relative, long size)> Save(IFormFile file, string folder, string name)
    {
        var rel = Path.Combine(folder, name);
        await using var fs = File.Create(PathFor(rel));
        await file.CopyToAsync(fs);
        return (rel.Replace('\\', '/'), fs.Length);
    }

    public FileStream Open(string relative) => File.OpenRead(PathFor(relative));
}
