using System.Numerics;
using System.Text.Json;
namespace MystieHook.Games.Wardog;

internal static class WardogRadarPosition
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MystieHook","minimap-position.json");
    internal sealed record Position(float X,float Y);
    internal static readonly Task<Position> Loaded=Task.Run(() =>
    {
        try
        {
            var p=JsonSerializer.Deserialize<Position>(File.ReadAllText(FilePath));
            return p!=null && float.IsFinite(p.X)&&float.IsFinite(p.Y)&&p.X is >=0 and <=16000&&p.Y is >=0 and <=16000?p:null;
        }
        catch { return null; }
    });
    private static Task _save=Task.CompletedTask;
    internal static void Save(Vector2 point)
    {
        var p=new Position(point.X,point.Y);
        _save=_save.ContinueWith(_ =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath+".tmp",JsonSerializer.Serialize(p));
                File.Move(FilePath+".tmp",FilePath,true);
            }
            catch { }
        },TaskScheduler.Default);
    }
}
