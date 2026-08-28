using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddAntiforgery();

var app = builder.Build();

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





app.Run();
