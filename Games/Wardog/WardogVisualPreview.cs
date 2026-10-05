using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace MystieHook.Games.Wardog;

// Synthetic, local-only sample. No attachment, DMA or live player data is used.
public static class WardogVisualPreview
{
    private static Texture2D _texture;
    private static bool _attempted;
    private static Vector2 _cachedSize;
    private static WardogReader.Frame _sample;
    private static readonly Vector2[] Bones =
    [ new(.5f,.105f),new(.5f,.18f),new(.5f,.25f),new(.5f,.32f),new(.5f,.4f),new(.5f,.48f),
      new(.39f,.53f),new(.36f,.7f),new(.31f,.93f),new(.27f,.95f),
      new(.36f,.22f),new(.28f,.27f),new(.13f,.36f),new(.055f,.425f),
      new(.59f,.53f),new(.63f,.7f),new(.7f,.93f),new(.74f,.95f),
      new(.64f,.22f),new(.73f,.27f),new(.87f,.36f),new(.95f,.425f) ];

    public static void LoadImage()
    {
        if(_attempted)return;
        _attempted=true;
        string path=Path.Combine(AppContext.BaseDirectory,"Assets","Img","PreviewCharacter.png");
        if(File.Exists(path))_texture=Raylib.LoadTexture(path);
    }
    public static void Shutdown()
    {
        if(_texture.Id!=0)Raylib.UnloadTexture(_texture);
        _texture=default;_attempted=false;_sample=null;
    }
    public static void Draw(ImDrawListPtr draw,Vector2 origin,Vector2 size,WardogVisualSettings visuals,bool enabled)
    {
        float height=Math.Max(40,Math.Min(size.Y-130,(size.X-36)*1639/960));
        var imageSize=new Vector2(height*960/1639,height);
        var imageOrigin=new Vector2((size.X-imageSize.X)/2,65+(size.Y-130-height)/2);
        draw.PushClipRect(origin,origin+size,true);
        if(_texture.Id!=0)draw.AddImage((nint)_texture.Id,origin+imageOrigin,origin+imageOrigin+imageSize);
        else draw.AddRect(origin+imageOrigin,origin+imageOrigin+imageSize,0xFF707070);
        if(_sample==null || _cachedSize!=size)
        {
            _cachedSize=size;
            WardogReader.Vec World(Vector2 point)=>new(){X=size.X/2,Y=point.X-size.X/2,Z=size.Y/2-point.Y};
            var points=new WardogReader.Vec[22];var valid=new bool[22];
            for(int i=0;i<22;i++){points[i]=World(imageOrigin+Bones[i]*imageSize);valid[i]=i is not(9 or 13 or 17 or 21);}
            var root=World(imageOrigin+new Vector2(.5f,.5f)*imageSize);
            var pose=new WardogReader.SkeletonPose(points,valid);
            var player=new WardogReader.Player(1,"Sample player",root,75)
            {Faction="Valkyra",CurrentItem="Sample item",TeamRelation=WardogReader.Relation.Enemy,Pose=pose,HeadPose=pose};
            _sample=new("Preview",true,new(default,default,90),root with {X=root.X-2500},[player]);
        }
        if(enabled)WardogOverlay.DrawPreview(_sample,visuals,draw,origin,size);
        draw.PopClipRect();
    }
}
