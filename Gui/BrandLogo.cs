using ImGuiNET;
using Raylib_cs;
using System.Numerics;
namespace MystieHook.Gui;
internal static class BrandLogo
{
 private static Texture2D _texture;private static bool _attempted;
 public static void Draw(float size)
 {
  if(!_attempted){_attempted=true;string path=Path.Combine(AppContext.BaseDirectory,"Assets","Img","MystieHookMark.png");if(File.Exists(path))_texture=Raylib.LoadTexture(path);}
  if(_texture.Id!=0)ImGui.Image((nint)_texture.Id,new Vector2(size));else ImGui.Dummy(new Vector2(size));
 }
 public static void Unload(){if(_texture.Id!=0)Raylib.UnloadTexture(_texture);_texture=default;_attempted=false;}
}
