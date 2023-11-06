using System;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

var sourceDirectory = "../";
var destinationDirectory = "../../src/Textures";
var skipDirectory = "./TextureConverter";

TraverseAndResize(sourceDirectory, destinationDirectory);

void TraverseAndResize(string source, string dest)
{
    // Ensure the destination directory exists
    Directory.CreateDirectory(dest);

    // Find and process PNG and JPEG files
    var imageFiles = Directory.GetFiles(source, "*.*")
        .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));

    foreach (var filePath in imageFiles)
    {
        ResizeAndSave(filePath, Path.Combine(dest, Path.GetFileNameWithoutExtension(filePath) + ".png"));
    }

    // Recursively process sub-directories
    var subDirectories = Directory.GetDirectories(source)
        .Where(dir => !dir.EndsWith(skipDirectory, StringComparison.OrdinalIgnoreCase));

    foreach (var dirPath in subDirectories)
    {
        TraverseAndResize(dirPath, Path.Combine(dest, Path.GetFileName(dirPath)));
    }
}

void ResizeAndSave(string sourcePath, string destPath)
{
    using var image = Image.Load(sourcePath);
    image.Mutate(x => x.Resize(new ResizeOptions
    {
        Size = new Size(256, 256),
        Mode = ResizeMode.Max
    }));
    image.Save(destPath, new PngEncoder());
}
