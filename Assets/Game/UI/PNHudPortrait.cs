#if UNITY_5_3_OR_NEWER
using UnityEngine;

internal static class PNHudPortrait
{
    // Read alpha once via GPU so source imports can remain non-readable. The generated
    // Sprite references the original texture; artwork/GUIDs are never rewritten.
    public static Sprite TrimTransparentMargin(Sprite source)
    {
        if(source.packed)return source;
        var texture=source.texture;
        var rect=source.textureRect;
        var previous=RenderTexture.active;
        RenderTexture target=null; Texture2D copy=null;
        try
        {
            target=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            Graphics.Blit(texture,target); RenderTexture.active=target;
            copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);
            copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); copy.Apply();
            var pixels=copy.GetPixels32();
            int left=Mathf.RoundToInt(rect.x),bottom=Mathf.RoundToInt(rect.y);
            int right=left+Mathf.RoundToInt(rect.width),top=bottom+Mathf.RoundToInt(rect.height);
            int minX=right,minY=top,maxX=left-1,maxY=bottom-1;
            for(int y=bottom;y<top;y++)for(int x=left;x<right;x++)
                if(pixels[y*texture.width+x].a>=16)
                { minX=Mathf.Min(minX,x);minY=Mathf.Min(minY,y);maxX=Mathf.Max(maxX,x);maxY=Mathf.Max(maxY,y); }
            if(maxX<minX || (minX==left && minY==bottom && maxX==right-1 && maxY==top-1))return source;
            var result=Sprite.Create(texture,new Rect(minX,minY,maxX-minX+1,maxY-minY+1),new Vector2(.5f,.5f),source.pixelsPerUnit,0,SpriteMeshType.FullRect);
            result.name="HUD trimmed "+source.name; result.hideFlags=HideFlags.DontSave; return result;
        }
        finally
        {
            RenderTexture.active=previous;
            if(copy!=null)PNUiRuntime.Retire(copy);
            if(target!=null)RenderTexture.ReleaseTemporary(target);
        }
    }
}
#endif
