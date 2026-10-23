using System;
using System.Collections.Generic;
using EveEngine;

// One source for pre-content Settings and registered built-ins; no mutable registry or Unity resource.
public static class PNMobileStyleDefaults
{
    public const string IconSet="project_n:mobile_icons_shapes", Palette="project_n:mobile_palette_u";
    public static IReadOnlyList<PNMobileIconSetDefinition> IconSets { get; }=Array.AsReadOnly(BuildIcons());
    public static IReadOnlyList<PNMobilePaletteDefinition> Palettes { get; }=Array.AsReadOnly(BuildPalettes());
    private static PNIconCommand M(float x,float y)=>new PNIconCommand(PNIconCommandKind.Move,x,y);
    private static PNIconCommand L(float x,float y)=>new PNIconCommand(PNIconCommandKind.Line,x,y);
    private static PNIconCommand C(float x,float y,float a,float b,float c,float d)=>new PNIconCommand(PNIconCommandKind.Cubic,x,y,a,b,c,d);
    private static PNIconCommand Z()=>new PNIconCommand(PNIconCommandKind.Close);
    private static PNMobileIconGeometry Polygon(params float[] xy)
    {
        int n=xy.Length/2;var c=new List<PNIconCommand>();
        for(int i=0;i<n;i++)
        {
            int prev=(i+n-1)%n,next=(i+1)%n;float x=xy[2*i],y=xy[2*i+1];
            float ax=x+(xy[2*prev]-x)*.15f,ay=y+(xy[2*prev+1]-y)*.15f;
            float bx=x+(xy[2*next]-x)*.15f,by=y+(xy[2*next+1]-y)*.15f;
            c.Add(i==0?M(ax,ay):L(ax,ay));c.Add(new PNIconCommand(PNIconCommandKind.Quadratic,x,y,bx,by));
        }
        c.Add(Z());return new PNMobileIconGeometry(new PNIconPath(c));
    }
    private static PNIconPath Letter(params float[] xy)
    {var c=new List<PNIconCommand>{M(xy[0],xy[1])};for(int i=2;i<xy.Length;i+=2)c.Add(L(xy[i],xy[i+1]));return new PNIconPath(c,false,.16f);}
    private static PNMobileIconSetDefinition[] BuildIcons()
    {
        var circle=new PNMobileIconGeometry(new PNIconPath(new[]{M(.5f,.08f),C(.732f,.08f,.92f,.268f,.92f,.5f),C(.92f,.732f,.732f,.92f,.5f,.92f),C(.268f,.92f,.08f,.732f,.08f,.5f),C(.08f,.268f,.268f,.08f,.5f,.08f),Z()}));
        var shapes=new[]{circle,Polygon(.5f,.08f,.94f,.86f,.06f,.86f),Polygon(.5f,.04f,.9f,.5f,.5f,.96f,.1f,.5f),Polygon(.14f,.14f,.86f,.14f,.86f,.86f,.14f,.86f),Polygon(.5f,.05f,.93f,.36f,.77f,.88f,.23f,.88f,.07f,.36f)};
        // Thick round-capped strokes form solid rounded letters with consistent visual weight.
        var letters=new[]{
            new PNMobileIconGeometry(Letter(.18f,.86f,.5f,.14f,.82f,.86f),Letter(.32f,.58f,.68f,.58f)),
            new PNMobileIconGeometry(new PNIconPath(new[]{M(.25f,.14f),L(.25f,.86f),L(.56f,.86f),C(.91f,.86f,.91f,.5f,.56f,.5f),L(.25f,.5f),L(.55f,.5f),C(.85f,.5f,.85f,.14f,.55f,.14f),L(.25f,.14f)},false,.16f)),
            new PNMobileIconGeometry(new PNIconPath(new[]{M(.78f,.22f),C(.05f,0,.05f,1,.78f,.78f)},false,.16f)),
            new PNMobileIconGeometry(new PNIconPath(new[]{M(.24f,.14f),L(.24f,.86f),C(.97f,.94f,.97f,.06f,.24f,.14f)},false,.16f)),
            new PNMobileIconGeometry(Letter(.77f,.14f,.24f,.14f,.24f,.86f,.77f,.86f),Letter(.24f,.5f,.67f,.5f))};
        return new[]{new PNMobileIconSetDefinition(EveId.Parse(IconSet),"project_n:mobile.icons.shapes",shapes),new PNMobileIconSetDefinition(EveId.Parse("project_n:mobile_icons_letters"),"project_n:mobile.icons.letters",letters,1)};
    }
    private static PNMobilePaletteDefinition[] BuildPalettes()
    {
        string[][] colors={new[]{"e1bbee","fddbd1","b8e1e7","baddc3","9addc3"},new[]{"7fe8e0","7fc0ff","a79dff","cf9bff","ff9fd6"},new[]{"ff6b5e","5a9bff","ffc83d","4fd39a","b07cff"},new[]{"b7d8c0","a3cfd0","9fbfdc","b5b3de","cbb0d2"}};
        string[] ids={"u","d","a","k"};var result=new PNMobilePaletteDefinition[4];
        for(int i=0;i<4;i++){var rgb=new PNMobileRgb[5];for(int j=0;j<5;j++){int v=Convert.ToInt32(colors[i][j],16);rgb[j]=new PNMobileRgb((v>>16)/255f,((v>>8)&255)/255f,(v&255)/255f);}result[i]=new PNMobilePaletteDefinition(EveId.Parse("project_n:mobile_palette_"+ids[i]),"project_n:mobile.palette."+ids[i],rgb,i);}
        return result;
    }
}
