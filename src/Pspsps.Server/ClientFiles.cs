using Microsoft.AspNetCore.StaticFiles;

namespace Pspsps.Server;

/// <summary>How the client's built files (wwwroot) are served.</summary>
public static class ClientFiles
{
    /// <summary>
    /// ASP.NET Core's content types, and the glTF binary (<c>.glb</c>, the cat model) it doesn't know: a file of an unknown
    /// type isn't served, and the request falls through to <c>index.html</c>.
    /// </summary>
    public static StaticFileOptions Options()
    {
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".glb"] = "model/gltf-binary";
        return new StaticFileOptions { ContentTypeProvider = types };
    }
}
