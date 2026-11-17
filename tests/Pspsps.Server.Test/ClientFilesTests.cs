using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Pspsps.Server.Test;

[TestFixture]
public class ClientFilesTests
{
    private string webRoot = "";

    [SetUp]
    public void CreateWebRoot()
    {
        webRoot = Directory.CreateTempSubdirectory("pspsps-wwwroot-").FullName;
        Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
        File.WriteAllBytes(Path.Combine(webRoot, "assets", "cat-abc123.glb"), [0x67, 0x6c, 0x54, 0x46, 2, 0, 0, 0]);
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html>");
    }

    [TearDown]
    public void DeleteWebRoot() => Directory.Delete(webRoot, recursive: true);

    /// <summary>The client's files as Program.cs serves them (UseClientFiles), index.html as the fallback.</summary>
    private async Task<(HttpStatusCode Status, string? ContentType, byte[] Body)> Get(string path)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = webRoot });
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseClientFiles();
        app.MapFallbackToFile("index.html");
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsByteArrayAsync());
    }

    // ASP.NET Core doesn't know .glb: without the mapping the model's URL answered with index.html, and the room
    // couldn't read its cats.
    [Test]
    public async Task TheCatModel_IsServedAsGltfBinary()
    {
        var (status, contentType, body) = await Get("/assets/cat-abc123.glb");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(contentType, Is.EqualTo("model/gltf-binary"));
            Assert.That(body, Is.EqualTo(new byte[] { 0x67, 0x6c, 0x54, 0x46, 2, 0, 0, 0 }));
        });
    }

    [Test]
    public async Task TheRoot_IsIndexHtml()
    {
        var (status, contentType, _) = await Get("/");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(contentType, Is.EqualTo("text/html"));
        });
    }
}
