using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using EveEngine;

public enum PNMobileControlChannel { Attack, Skill, Ulti, Dash, Burst }
public enum PNIconCommandKind { Move, Line, Quadratic, Cubic, Close }

// Immutable normalized geometry. No Unity object, delegate or per-view state enters content hashes.
public sealed class PNIconCommand
{
    public PNIconCommandKind Kind { get; }
    public float X { get; } public float Y { get; }
    public float X2 { get; } public float Y2 { get; }
    public float X3 { get; } public float Y3 { get; }
    public PNIconCommand(PNIconCommandKind kind,float x=0,float y=0,float x2=0,float y2=0,float x3=0,float y3=0)
    {
        if(!Enum.IsDefined(typeof(PNIconCommandKind),kind))throw new ArgumentException("Unknown icon command.");
        foreach(float v in new[]{x,y,x2,y2,x3,y3})if(float.IsNaN(v)||float.IsInfinity(v)||v<0||v>1)throw new ArgumentOutOfRangeException(nameof(x));
        Kind=kind;X=x;Y=y;X2=x2;Y2=y2;X3=x3;Y3=y3;
    }
}
public sealed class PNIconPath
{
    public IReadOnlyList<PNIconCommand> Commands { get; }
    public bool Fill { get; }
    public float StrokeWidth { get; }
    public PNIconPath(IEnumerable<PNIconCommand> commands,bool fill=false,float strokeWidth=.055f)
    {
        var items=(commands??throw new ArgumentNullException(nameof(commands))).ToArray();
        if(items.Length<2||items.Length>128||items.Any(x=>x==null)||items[0].Kind!=PNIconCommandKind.Move||
           items.Count(x=>x.Kind==PNIconCommandKind.Move)!=1||items.Skip(1).All(x=>x.Kind==PNIconCommandKind.Close))throw new ArgumentException("Invalid icon path.");
        if(items.Take(items.Length-1).Any(x=>x.Kind==PNIconCommandKind.Close))throw new ArgumentException("Close must end a path.");
        if(float.IsNaN(strokeWidth)||float.IsInfinity(strokeWidth)||strokeWidth<0||strokeWidth>.2f||(!fill&&strokeWidth==0))throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        Commands=Array.AsReadOnly(items);Fill=fill;StrokeWidth=strokeWidth;
    }
}
public sealed class PNMobileIconGeometry
{
    public IReadOnlyList<PNIconPath> Paths { get; }
    public PNMobileIconGeometry(params PNIconPath[] paths)
    {if(paths==null||paths.Length==0||paths.Length>8||paths.Any(x=>x==null))throw new ArgumentException("Invalid icon geometry.");Paths=Array.AsReadOnly((PNIconPath[])paths.Clone());}
}
public sealed class PNMobileRgb
{
    public float R { get; } public float G { get; } public float B { get; }
    public PNMobileRgb(float r,float g,float b)
    {foreach(float v in new[]{r,g,b})if(float.IsNaN(v)||float.IsInfinity(v)||v<0||v>1)throw new ArgumentOutOfRangeException(nameof(r));R=r;G=g;B=b;}
}
public abstract class PNMobileStyleDefinition : EveIIdentified, EveIRegistrationReferences
{
    public EveId Id { get; }
    public string DisplayNameKey { get; }
    public int Order { get; }
    protected PNMobileStyleDefinition(EveId id,string displayNameKey,int order)
    {if(!id.IsValid||id.Namespace=="base")throw new ArgumentException("Invalid style ID.");EveId.Parse(displayNameKey);Id=id;DisplayNameKey=displayNameKey;Order=order;}
    public void ValidateRegistrationReferences(string owner,IReadOnlyCollection<string> dependencies)
    {string ns=EveId.Parse(DisplayNameKey).Namespace;if(ns!=owner&&ns!="base"&&!dependencies.Contains(ns))throw new InvalidOperationException("Style label requires a declared dependency: "+ns);}
    protected static IReadOnlyList<T> Five<T>(IEnumerable<T> source) where T:class
    {var a=(source??throw new ArgumentNullException(nameof(source))).ToArray();if(a.Length!=5||a.Any(x=>x==null))throw new ArgumentException("Exactly five channels are required, in enum order.");return Array.AsReadOnly(a);}
}
public sealed class PNMobileIconSetDefinition : PNMobileStyleDefinition
{
    public IReadOnlyList<PNMobileIconGeometry> Icons { get; }
    public PNMobileIconSetDefinition(EveId id,string displayNameKey,IEnumerable<PNMobileIconGeometry> icons,int order=0):base(id,displayNameKey,order){Icons=Five(icons);}
}
public sealed class PNMobilePaletteDefinition : PNMobileStyleDefinition
{
    public IReadOnlyList<PNMobileRgb> Colors { get; }
    public PNMobilePaletteDefinition(EveId id,string displayNameKey,IEnumerable<PNMobileRgb> colors,int order=0):base(id,displayNameKey,order){Colors=Five(colors);}
}

namespace EveEngine
{
    public static class PNMobileStyleRegistry
    {
        public static EveRegistry<PNMobileIconSetDefinition> MobileIconSets(this EveRegistryHub hub)=>hub.Get<PNMobileIconSetDefinition>();
        public static EveRegistry<PNMobilePaletteDefinition> MobilePalettes(this EveRegistryHub hub)=>hub.Get<PNMobilePaletteDefinition>();
    }
}
