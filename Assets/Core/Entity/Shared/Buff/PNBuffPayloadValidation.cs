using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Bounds are checked while visiting/writing, before cloning caller-owned data.
public static class PNBuffPayloadValidation
{
    public const int MaximumBytes=16384, MaximumDepth=32, MaximumNodes=4096;
    private sealed class BoundedText : StringWriter
    {
        private int bytes;
        private readonly int maximum;
        public BoundedText(int maximum) : base(System.Globalization.CultureInfo.InvariantCulture) { this.maximum=maximum; }
        public override void Write(char value) { Charge(value.ToString()); base.Write(value); }
        public override void Write(string value) { Charge(value); base.Write(value); }
        public override void Write(char[] value,int index,int count) { Charge(new string(value,index,count)); base.Write(value,index,count); }
        private void Charge(string value) { bytes=checked(bytes+Encoding.UTF8.GetByteCount(value??"")); if(bytes>maximum)throw new FormatException("Buff JSON byte bound."); }
    }
    private static void Visit(JToken token,int depth,ref int nodes)
    {
        if(token==null)return;
        if(depth>MaximumDepth||++nodes>MaximumNodes)throw new FormatException("Buff JSON structural bound.");
        switch(token.Type)
        {
            case JTokenType.Object: case JTokenType.Array: case JTokenType.Property:
                foreach(var child in token.Children())Visit(child,depth+1,ref nodes); break;
            case JTokenType.Null: case JTokenType.String: case JTokenType.Integer: case JTokenType.Boolean: break;
            case JTokenType.Float:
                double value=token.Value<double>(); if(double.IsNaN(value)||double.IsInfinity(value))throw new FormatException("Buff JSON finite number required."); break;
            default: throw new FormatException("Unsupported Buff JSON token: "+token.Type);
        }
    }
    public static byte[] Encode(JToken token,int maximum=MaximumBytes)
    {
        int nodes=0;Visit(token,0,ref nodes);
        using(var text=new BoundedText(maximum))
        using(var writer=new JsonTextWriter(text))
        { if(token==null)writer.WriteNull();else token.WriteTo(writer);writer.Flush();return new UTF8Encoding(false,true).GetBytes(text.ToString()); }
    }
    public static void Validate(JToken token) { Encode(token); }
    public static JToken Decode(byte[] bytes)
        => Decode(bytes,null);
    public static JToken Decode(byte[] bytes,Action<int> reserveNodes)
    {
        try { return DecodeCore(bytes,reserveNodes); } catch(JsonException error) { throw new FormatException("Invalid Buff JSON.",error); } catch(DecoderFallbackException error){throw new FormatException("Invalid Buff UTF-8.",error);}
    }
    private static JToken DecodeCore(byte[] bytes,Action<int> reserveNodes)
    {
        if(bytes==null||bytes.Length>MaximumBytes)throw new FormatException("Buff JSON byte bound.");
        var json=new UTF8Encoding(false,true).GetString(bytes);int nodes=0;
        using(var text=new StringReader(json))
        using(var reader=new JsonTextReader(text){MaxDepth=MaximumDepth,DateParseHandling=DateParseHandling.None})
        {while(reader.Read()){if(reader.TokenType==JsonToken.Comment)throw new FormatException("Buff JSON comments are unsupported.");if(reader.TokenType!=JsonToken.EndArray&&reader.TokenType!=JsonToken.EndObject&&++nodes>MaximumNodes)throw new FormatException("Buff JSON node bound.");}}
        reserveNodes?.Invoke(nodes);
        using(var text=new StringReader(json))
        using(var reader=new JsonTextReader(text){MaxDepth=MaximumDepth,DateParseHandling=DateParseHandling.None})
        {
            var token=JToken.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            if(reader.Read())throw new FormatException("Trailing Buff JSON data.");Validate(token);return token.Type==JTokenType.Null?null:token;
        }
    }
}
