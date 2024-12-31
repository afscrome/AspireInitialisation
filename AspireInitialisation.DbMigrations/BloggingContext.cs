using Microsoft.EntityFrameworkCore;

public class BloggingContext : DbContext
{
    public required DbSet<Blog> Blogs { get; set; }
    public required DbSet<Post> Posts { get; set; }

    public BloggingContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
    }
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        // connect to sql server with connection string from app settings
        options.UseSqlServer();
    }
}

public class Blog
{
    public required int BlogId { get; set; }
    public required string Url { get; set; }

    public List<Post> Posts { get; } = new();
}

public class Post
{
    public required int PostId { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }

    public required int BlogId { get; set; }
    public required Blog Blog { get; set; }
}