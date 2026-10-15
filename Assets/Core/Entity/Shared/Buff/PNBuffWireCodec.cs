using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EveEngine;

// Bounded JSON adapter for transports. CLR type names are never accepted from the wire.
public static class PNBuffWireCodec
{
    public const int ProtocolVersion = 1;
    private static JsonSerializerSettings Settings() => new JsonSerializerSettings
    { TypeNameHandling = TypeNameHandling.None, MaxDepth = 32, MissingMemberHandling = MissingMemberHandling.Error,
        Converters = { new IdentityConverter(), new NumbersConverter() } };
    private sealed class IdentityConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(EveId) || type == typeof(EveEntityId);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        { if (value is EveId id) writer.WriteValue(id.ToString()); else writer.WriteValue(((EveEntityId)value).Value); }
        public override object ReadJson(JsonReader reader, Type type, object existingValue, JsonSerializer serializer)
        {
            if (type == typeof(EveId)) { if (reader.TokenType != JsonToken.String) throw new FormatException("ID string required."); return EveId.Parse((string)reader.Value); }
            if (reader.TokenType != JsonToken.Integer) throw new FormatException("Entity ID integer required."); return new EveEntityId(Convert.ToInt64(reader.Value));
        }
    }
    private sealed class NumbersConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(PNBuffNumbers);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        { var n = (PNBuffNumbers)value; new JObject { ["TopLeft"] = n.TopLeft, ["TopRight"] = n.TopRight, ["BottomLeft"] = n.BottomLeft, ["BottomRight"] = n.BottomRight }.WriteTo(writer); }
        public override object ReadJson(JsonReader reader, Type type, object existingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader); PNBuffContentBatch.Fields(obj, "TopLeft", "TopRight", "BottomLeft", "BottomRight");
            long? Number(string key) { var token = obj[key] ?? throw new FormatException("Missing UI number slot."); if (token.Type == JTokenType.Null) return null; if (token.Type != JTokenType.Integer) throw new FormatException("Integer UI number required."); return token.Value<long>(); }
            return new PNBuffNumbers(Number("TopLeft"), Number("TopRight"), Number("BottomLeft"), Number("BottomRight"));
        }
    }
    private sealed class Envelope<T>
    {
        public int Protocol { get; set; }
        public T Message { get; set; }
    }
    public static byte[] Encode<T>(T message, int maximumBytes = 1048576) where T : class
    {
        if (message == null || maximumBytes < 1) throw new ArgumentException("Message and positive byte limit required.");
        if (typeof(T) != typeof(PNBuffWireMessage) && typeof(T) != typeof(PNBuffOptionalCueMessage)) throw new ArgumentException("Unsupported Buff wire message.");
        byte[] bytes;
        using (var text = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture))
        using (var writer = new JsonTextWriter(text))
        {
            writer.WriteStartObject(); Field(writer, "Protocol", ProtocolVersion); writer.WritePropertyName("Message"); writer.WriteStartObject();
            if (message is PNBuffWireMessage m)
            {
                Field(writer, "SessionId", m.SessionId); Field(writer, "TargetId", m.TargetId.Value); Field(writer, "Epoch", m.Epoch);
                Field(writer, "Sequence", m.Sequence); Field(writer, "Kind", (int)m.Kind);
                writer.WritePropertyName("States"); writer.WriteStartArray(); foreach (var state in m.States) WriteState(writer, state); writer.WriteEndArray();
                Field(writer, "RemovedId", m.RemovedId); Field(writer, "EndReason", m.EndReason.HasValue ? (object)(int)m.EndReason.Value : null);
                writer.WritePropertyName("Cues"); writer.WriteStartArray(); foreach (var cue in m.Cues) WriteCue(writer, cue); writer.WriteEndArray();
            }
            else
            {
                var cue = (PNBuffOptionalCueMessage)(object)message;
                Field(writer, "SessionId", cue.SessionId); Field(writer, "TargetId", cue.TargetId.Value); Field(writer, "Epoch", cue.Epoch);
                writer.WritePropertyName("Cue"); WriteCue(writer, cue.Cue);
            }
            writer.WriteEndObject(); writer.WriteEndObject(); writer.Flush(); bytes = Encoding.UTF8.GetBytes(text.ToString());
        }
        if (bytes.Length > maximumBytes) throw new FormatException("Buff envelope exceeds byte limit."); return bytes;
    }
    private static void Field(JsonWriter writer, string name, object value) { writer.WritePropertyName(name); writer.WriteValue(value); }
    private static void Token(JsonWriter writer, string name, JToken value) { writer.WritePropertyName(name); if (value == null) writer.WriteNull(); else value.WriteTo(writer); }
    private static void WriteState(JsonWriter writer, PNBuffWireState s)
    {
        if (s == null) { writer.WriteNull(); return; }
        writer.WriteStartObject(); Field(writer, "InstanceId", s.InstanceId); Field(writer, "Revision", s.Revision);
        Field(writer, "BuffId", s.BuffId.ToString()); Field(writer, "ProfileId", s.ProfileId.ToString()); Field(writer, "SourceId", s.SourceId.Value);
        Field(writer, "Stacks", s.Stacks); Field(writer, "Suspended", s.Suspended); writer.WritePropertyName("Numbers"); writer.WriteStartObject();
        Field(writer, "TopLeft", s.Numbers.TopLeft); Field(writer, "TopRight", s.Numbers.TopRight); Field(writer, "BottomLeft", s.Numbers.BottomLeft); Field(writer, "BottomRight", s.Numbers.BottomRight); writer.WriteEndObject();
        Field(writer, "Clock", (int)s.Clock); Field(writer, "BuffTime", s.BuffTime); Field(writer, "AnchorTime", s.AnchorTime); Field(writer, "Rate", s.Rate);
        Field(writer, "Expiration", s.Expiration); Field(writer, "PayloadVersion", s.PayloadVersion); Token(writer, "DynamicData", s.DynamicData); writer.WriteEndObject();
    }
    private static void WriteCue(JsonWriter writer, PNBuffCue cue)
    {
        if (cue == null) { writer.WriteNull(); return; }
        writer.WriteStartObject(); Field(writer, "Id", cue.Id); Field(writer, "InstanceId", cue.InstanceId); Field(writer, "Name", cue.Name);
        Field(writer, "At", cue.At); Field(writer, "MaximumAge", cue.MaximumAge); Token(writer, "Payload", cue.Payload); writer.WriteEndObject();
    }
    public static T Decode<T>(byte[] bytes, int maximumBytes = 1048576) where T : class
    {
        if (bytes == null || maximumBytes < 1 || bytes.Length > maximumBytes) throw new FormatException("Invalid Buff envelope size.");
        if (typeof(T) != typeof(PNBuffWireMessage) && typeof(T) != typeof(PNBuffOptionalCueMessage)) throw new ArgumentException("Unsupported Buff wire message.");
        var json = new UTF8Encoding(false, true).GetString(bytes);
        JObject root;
        using (var text = new System.IO.StringReader(json))
        using (var reader = new JsonTextReader(text) { MaxDepth = 32 }) root = JObject.Load(reader);
        void Required(JObject obj, params string[] names) { PNBuffContentBatch.Fields(obj, names); foreach (var name in names) if (obj.Property(name) == null) throw new FormatException("Missing wire field " + name); }
        Required(root, "Protocol", "Message");
        var message = PNBuffContentBatch.Object(root["Message"]);
        if (typeof(T) == typeof(PNBuffWireMessage))
        {
            Required(message, "SessionId", "TargetId", "Epoch", "Sequence", "Kind", "States", "RemovedId", "EndReason", "Cues");
            if (!(message["States"] is JArray states) || !(message["Cues"] is JArray cues)) throw new FormatException("Wire arrays required.");
            foreach (var state in states) Required(PNBuffContentBatch.Object(state), "InstanceId", "Revision", "BuffId", "ProfileId", "SourceId", "Stacks", "Suspended", "Numbers", "Clock", "BuffTime", "AnchorTime", "Rate", "Expiration", "PayloadVersion", "DynamicData");
            foreach (var cue in cues) Required(PNBuffContentBatch.Object(cue), "Id", "InstanceId", "Name", "At", "MaximumAge", "Payload");
        }
        else { Required(message, "SessionId", "TargetId", "Epoch", "Cue"); Required(PNBuffContentBatch.Object(message["Cue"]), "Id", "InstanceId", "Name", "At", "MaximumAge", "Payload"); }
        var value = JsonConvert.DeserializeObject<Envelope<T>>(json, Settings());
        if (value == null || value.Protocol != ProtocolVersion || value.Message == null) throw new FormatException("Unsupported or incomplete Buff envelope.");
        return value.Message;
    }
}
