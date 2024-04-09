using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAntiforgery();
var app = builder.Build();
app.UseStaticFiles();

var imageManager = new ImageManager("uploadedImages.json");

app.MapGet("/", async (HttpContext context, IAntiforgery antiforgery) =>
{
    var token = antiforgery.GetAndStoreTokens(context)!;
    var html = $@"
        <html>
        <body>
            <form action=""/upload"" method=""post"" enctype=""multipart/form-data"">
                <input name=""{token.FormFieldName}"" type=""hidden"" value=""{token.RequestToken}"" />
                <input type=""file"" name=""file"" accept="".jpg, .jpeg, .png, .gif"" required/>
                <input type=""text"" name=""title"" placeholder=""Title"" required/>
                <button type=""submit"">Upload Image</button>
            </form>
        </body>
        </html>
    ";
    await context.Response.WriteAsync(html);
});

app.MapPost("/upload", async (HttpContext context, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);

    var form = await context.Request.ReadFormAsync();
    var title = form["title"];
    var file = form.Files["file"];

    if (file == null || file.Length == 0)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("No file uploaded.");
        return;
    }

    if (file.Length > 10 * 1024 * 1024)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("File size exceeds the limit of 10MB.");
        return;
    }

    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
    var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (!allowedExtensions.Contains(fileExtension))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Invalid file type. Only JPG, JPEG, PNG, or GIF are allowed.");
        return;
    }

    try
    {
        var imageId = Guid.NewGuid().ToString();
        var imageDirectory = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "images");
        if (!Directory.Exists(imageDirectory))
        {
            Directory.CreateDirectory(imageDirectory);
        }

        var imagePath = Path.Combine(imageDirectory, imageId + fileExtension);
        await using (var fileStream = new FileStream(imagePath, FileMode.Create))
        {
            await file.CopyToAsync(fileStream);
        }

        var uploadedImage = new UploadedImage
        {
            Id = imageId,
            Title = title,
            FileName = file.FileName
        };
        imageManager.AddImage(uploadedImage);

        context.Response.Redirect($"/picture/{imageId}");
        return;
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsync($"Error uploading file: {ex.Message}");
        return;
    }
});

app.MapGet("/picture/{id}", async (HttpContext context) =>
{
    var id = context.Request.RouteValues["id"] as string;
    if (string.IsNullOrEmpty(id))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var image = imageManager.UploadedImages.FirstOrDefault(img => img.Id == id);
    if (image == null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var imagePath = $"/images/{id}{Path.GetExtension(image.FileName)}";
    var html = $@"
        <html>
        <head>
            <title>{image.Title}</title>
        </head>
        <body>
            <h1>{image.Title}</h1>
            <img src=""{imagePath}"" alt=""{image.Title}"" />
        </body>
        </html>";

    await context.Response.WriteAsync(html);
});

app.Run();
public class UploadedImage
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? FileName { get; set; }
}

public class ImageManager
{
    private readonly string _storageFilePath;
    private readonly List<UploadedImage> _uploadedImages;

    public ImageManager(string storageFilePath)
    {
        _storageFilePath = storageFilePath;
        _uploadedImages = LoadImagesFromStorage();
    }

    public List<UploadedImage> UploadedImages => _uploadedImages;

    private List<UploadedImage> LoadImagesFromStorage()
    {
        if (!File.Exists(_storageFilePath))
            return new List<UploadedImage>();

        string json = File.ReadAllText(_storageFilePath);
        return JsonSerializer.Deserialize<List<UploadedImage>>(json) ?? new List<UploadedImage>();
    }

    public void SaveImagesToStorage()
    {
        string json = JsonSerializer.Serialize(_uploadedImages);
        File.WriteAllText(_storageFilePath, json);
    }

    public void AddImage(UploadedImage image)
    {
        _uploadedImages.Add(image);
        SaveImagesToStorage();
    }
}
