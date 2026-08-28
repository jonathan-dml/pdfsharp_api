using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactClient", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:5173", // Vite's default dev port
                  "https://green-sky-052853c10.7.azurestaticapps.net")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
builder.Services.AddOpenApi();
builder.Services.AddAntiforgery();

var app = builder.Build();
app.UseCors("ReactClient");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapGet("/", () =>
{
    return "Toolkit for working with PDF files.";
});

app.MapPost("/merge", async (IFormFile file1, IFormFile file2) =>
{
    if (file1.Length == 0 || file2.Length == 0)
    {
        return Results.BadRequest("Both PDF files are required and must not be empty.");
    }

    try
    {
        using var mergedDocument = new PdfDocument();

        foreach (var file in new[] { file1, file2 })
        {
            await using var fileStream = file.OpenReadStream();
            using var sourceDocument = PdfReader.Open(fileStream, PdfDocumentOpenMode.Import);

            foreach (var page in sourceDocument.Pages)
            {
                mergedDocument.AddPage(page);
            }
        }

        await using var outputStream = new MemoryStream();
        mergedDocument.Save(outputStream, false);

        return Results.File(outputStream.ToArray(), "application/pdf", "merged.pdf");
    }
    catch (InvalidOperationException)
    {
        return Results.BadRequest("Both files must be valid PDF documents.");
    }
})
.DisableAntiforgery();

app.MapPost("/split", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("The request must use multipart/form-data.");
    }

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    var pageGroupsJson = form["pageGroups"].ToString();

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("A non-empty PDF file is required.");
    }

    if (string.IsNullOrWhiteSpace(pageGroupsJson))
    {
        return Results.BadRequest("The pageGroups dictionary is required as a JSON object.");
    }

    Dictionary<int, int>? pageGroups;
    try
    {
        pageGroups = JsonSerializer.Deserialize<Dictionary<int, int>>(pageGroupsJson);
    }
    catch (JsonException)
    {
        return Results.BadRequest("pageGroups must be a valid JSON object with page numbers as keys.");
    }

    if (pageGroups is null || pageGroups.Count == 0 || pageGroups.Keys.Any(page => page < 1) || pageGroups.Values.Any(group => group < 1))
    {
        return Results.BadRequest("pageGroups must contain positive page numbers and group numbers.");
    }

    try
    {
        await using var fileStream = file.OpenReadStream();
        using var sourceDocument = PdfReader.Open(fileStream, PdfDocumentOpenMode.Import);

        if (pageGroups.Count != sourceDocument.PageCount || pageGroups.Keys.Any(page => page > sourceDocument.PageCount))
        {
            return Results.BadRequest("pageGroups must assign every page in the PDF exactly once.");
        }

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            foreach (var group in pageGroups.Values.Distinct().Order())
            {
                var entry = archive.CreateEntry($"group-{group}.pdf", CompressionLevel.Fastest);
                using var groupDocument = new PdfDocument();

                foreach (var pageNumber in pageGroups
                    .Where(page => page.Value == group)
                    .Select(page => page.Key)
                    .Order())
                {
                    groupDocument.AddPage(sourceDocument.Pages[pageNumber - 1]);
                }

                using var groupStream = new MemoryStream();
                groupDocument.Save(groupStream, false);
                groupStream.Position = 0;

                await using var entryStream = entry.Open();
                await groupStream.CopyToAsync(entryStream);
            }
        }

        return Results.File(zipStream.ToArray(), "application/zip", "split-pdfs.zip");
    }
    catch (Exception exception) when (exception is InvalidOperationException or PdfReaderException)
    {
        return Results.BadRequest("The file must be a valid PDF document.");
    }
})
.DisableAntiforgery();

app.MapPost("/extract", async (IFormFile file, [FromForm] int[] selectedPages) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("A non-empty PDF file is required.");
    }

    if (selectedPages is null || selectedPages.Length == 0)
    {
        return Results.BadRequest("At least one page number must be selected.");
    }

    if (selectedPages.Any(page => page < 1) || selectedPages.Distinct().Count() != selectedPages.Length)
    {
        return Results.BadRequest("selectedPages must contain positive, unique page numbers.");
    }

    try
    {
        await using var fileStream = file.OpenReadStream();
        using var sourceDocument = PdfReader.Open(fileStream, PdfDocumentOpenMode.Import);

        if (selectedPages.Any(page => page > sourceDocument.PageCount))
        {
            return Results.BadRequest($"selectedPages must contain page numbers from 1 to {sourceDocument.PageCount}.");
        }

        using var extractedDocument = new PdfDocument();

        foreach (var pageNumber in selectedPages)
        {
            extractedDocument.AddPage(sourceDocument.Pages[pageNumber - 1]);
        }

        await using var outputStream = new MemoryStream();
        extractedDocument.Save(outputStream, false);

        return Results.File(outputStream.ToArray(), "application/pdf", "extracted.pdf");
    }
    catch (Exception exception) when (exception is InvalidOperationException or PdfReaderException)
    {
        return Results.BadRequest("The file must be a valid PDF document.");
    }
})
.DisableAntiforgery();

app.MapPost("/delete", async (IFormFile file, [FromForm] int[] selectedPages) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("A non-empty PDF file is required.");
    }

    if (selectedPages is null || selectedPages.Length == 0)
    {
        return Results.BadRequest("At least one page number must be selected for deletion.");
    }

    if (selectedPages.Any(page => page < 1) || selectedPages.Distinct().Count() != selectedPages.Length)
    {
        return Results.BadRequest("selectedPages must contain positive, unique page numbers.");
    }

    try
    {
        await using var fileStream = file.OpenReadStream();
        using var sourceDocument = PdfReader.Open(fileStream, PdfDocumentOpenMode.Import);

        if (selectedPages.Any(page => page > sourceDocument.PageCount))
        {
            return Results.BadRequest($"selectedPages must contain page numbers from 1 to {sourceDocument.PageCount}.");
        }

        var pagesToKeep = Enumerable.Range(1, sourceDocument.PageCount)
            .Except(selectedPages)
            .ToArray();

        if (pagesToKeep.Length == 0)
        {
            return Results.BadRequest("At least one page must remain in the resulting PDF.");
        }

        using var resultDocument = new PdfDocument();

        foreach (var pageNumber in pagesToKeep)
        {
            resultDocument.AddPage(sourceDocument.Pages[pageNumber - 1]);
        }

        await using var outputStream = new MemoryStream();
        resultDocument.Save(outputStream, false);

        return Results.File(outputStream.ToArray(), "application/pdf", "deleted-pages-removed.pdf");
    }
    catch (Exception exception) when (exception is InvalidOperationException or PdfReaderException)
    {
        return Results.BadRequest("The file must be a valid PDF document.");
    }
})
.DisableAntiforgery();

app.MapPost("/rotate", async (IFormFile file, [FromForm] string rotationAngles) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("A non-empty PDF file is required.");
    }

    if (string.IsNullOrWhiteSpace(rotationAngles))
    {
        return Results.BadRequest("The rotationAngles dictionary is required as a JSON object.");
    }

    Dictionary<int, int>? rotations;
    try
    {
        rotations = JsonSerializer.Deserialize<Dictionary<int, int>>(rotationAngles);
    }
    catch (JsonException)
    {
        return Results.BadRequest("rotationAngles must be a valid JSON object with page numbers as keys.");
    }

    if (rotations is null || rotations.Count == 0)
    {
        return Results.BadRequest("rotationAngles must contain at least one page rotation.");
    }

    if (rotations.Keys.Any(page => page < 1))
    {
        return Results.BadRequest("rotationAngles must contain positive page numbers.");
    }

    if (rotations.Values.Any(angle => angle is not (0 or 90 or 180 or 270)))
    {
        return Results.BadRequest("Rotation angles must be 0, 90, 180, or 270 degrees.");
    }

    try
    {
        await using var fileStream = file.OpenReadStream();
        using var sourceDocument = PdfReader.Open(fileStream, PdfDocumentOpenMode.Import);

        if (rotations.Keys.Any(page => page > sourceDocument.PageCount))
        {
            return Results.BadRequest($"rotationAngles must contain page numbers from 1 to {sourceDocument.PageCount}.");
        }

        using var rotatedDocument = new PdfDocument();

        for (var pageNumber = 1; pageNumber <= sourceDocument.PageCount; pageNumber++)
        {
            var page = sourceDocument.Pages[pageNumber - 1];

            if (rotations.TryGetValue(pageNumber, out var rotationAngle))
            {
                page.Rotate = rotationAngle;
            }

            rotatedDocument.AddPage(page);
        }

        await using var outputStream = new MemoryStream();
        rotatedDocument.Save(outputStream, false);

        return Results.File(outputStream.ToArray(), "application/pdf", "rotated.pdf");
    }
    catch (Exception exception) when (exception is InvalidOperationException or PdfReaderException)
    {
        return Results.BadRequest("The file must be a valid PDF document.");
    }
})
.DisableAntiforgery();

app.MapPost("/reorder", async (IFormFile file, [FromForm] string newOrder) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("A non-empty PDF file is required.");
    }

    if (string.IsNullOrWhiteSpace(newOrder))
    {
        return Results.BadRequest("The newOrder dictionary is required as a JSON object.");
    }

    Dictionary<int, int>? pageOrder;
    try
    {
        pageOrder = JsonSerializer.Deserialize<Dictionary<int, int>>(newOrder);
    }
    catch (JsonException)
    {
        return Results.BadRequest("newOrder must be a valid JSON object with old page numbers as keys.");
    }

    if (pageOrder is null || pageOrder.Count == 0)
    {
        return Results.BadRequest("newOrder must contain at least one page mapping.");
    }

    if (pageOrder.Keys.Any(page => page < 1) || pageOrder.Values.Any(position => position < 1))
    {
        return Results.BadRequest("newOrder must contain positive page numbers.");
    }

    if (pageOrder.Values.Distinct().Count() != pageOrder.Count)
    {
        return Results.BadRequest("Each new page number must be unique.");
    }

    try
    {
        await using var fileStream = file.OpenReadStream();
        using var sourceDocument = PdfReader.Open(fileStream, PdfDocumentOpenMode.Import);

        var pageCount = sourceDocument.PageCount;
        var expectedPageNumbers = Enumerable.Range(1, pageCount).ToHashSet();

        if (!pageOrder.Keys.ToHashSet().SetEquals(expectedPageNumbers) ||
            !pageOrder.Values.ToHashSet().SetEquals(expectedPageNumbers))
        {
            return Results.BadRequest($"newOrder must map every page from 1 to {pageCount} to a unique position in the same range.");
        }

        using var reorderedDocument = new PdfDocument();

        foreach (var oldPageNumber in pageOrder
            .OrderBy(page => page.Value)
            .Select(page => page.Key))
        {
            reorderedDocument.AddPage(sourceDocument.Pages[oldPageNumber - 1]);
        }

        await using var outputStream = new MemoryStream();
        reorderedDocument.Save(outputStream, false);

        return Results.File(outputStream.ToArray(), "application/pdf", "reordered.pdf");
    }
    catch (Exception exception) when (exception is InvalidOperationException or PdfReaderException)
    {
        return Results.BadRequest("The file must be a valid PDF document.");
    }
})
.DisableAntiforgery();

app.MapPost("/copy", async (
    IFormFile fileToCopy,
    IFormFile targetFile,
    [FromForm] int[] selectedPages) =>
{
    if (fileToCopy is null || fileToCopy.Length == 0)
    {
        return Results.BadRequest("A non-empty source PDF file is required.");
    }

    if (targetFile is null || targetFile.Length == 0)
    {
        return Results.BadRequest("A non-empty target PDF file is required.");
    }

    if (selectedPages is null || selectedPages.Length == 0)
    {
        return Results.BadRequest("At least one page number must be selected.");
    }

    if (selectedPages.Any(page => page < 1) || selectedPages.Distinct().Count() != selectedPages.Length)
    {
        return Results.BadRequest("selectedPages must contain positive, unique page numbers.");
    }

    try
    {
        await using var sourceStream = fileToCopy.OpenReadStream();
        using var sourceDocument = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Import);

        if (selectedPages.Any(page => page > sourceDocument.PageCount))
        {
            return Results.BadRequest($"selectedPages must contain page numbers from 1 to {sourceDocument.PageCount}.");
        }

        await using var targetStream = targetFile.OpenReadStream();
        using var targetDocument = PdfReader.Open(targetStream, PdfDocumentOpenMode.Modify);

        foreach (var pageNumber in selectedPages)
        {
            targetDocument.AddPage(sourceDocument.Pages[pageNumber - 1]);
        }

        await using var outputStream = new MemoryStream();
        targetDocument.Save(outputStream, false);

        return Results.File(outputStream.ToArray(), "application/pdf", "copied-pages.pdf");
    }
    catch (Exception exception) when (exception is InvalidOperationException or PdfReaderException)
    {
        return Results.BadRequest("Both files must be valid PDF documents.");
    }
})
.DisableAntiforgery();





app.Run();
