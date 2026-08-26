using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

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



app.Run();
