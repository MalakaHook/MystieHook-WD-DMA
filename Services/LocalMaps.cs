namespace MystieHook.Services;

// Resolve installed files only. No download, startup gate, or disk I/O on the UI thread.
public static class LocalMaps
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MystieHook", "Maps");
    public static string PreparedDirectory(string map)
    {
        string version=map switch
        {
        "bakurani" => "a120377d9e0ad3d09c1bb36643094dc2ac083854338a2889a3ac748e1f937615",
        "ozeti" => "dc8fc3fe5ddb169aea36f97c9bd8465d3be18e5a635f30fa70c792bfe6517c0d",
        "zestafona" => "d021201845a81cefc1942da60c7fa178a4007a978c0d584f46ee3cfe54cbccfe",
            _ => null
        };
        return version==null ? null : Path.Combine(Root,map,version);
    }
}