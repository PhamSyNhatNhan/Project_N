using System;
using Newtonsoft.Json.Linq;

// One default payload shared by registration and backward-compatible derived profiles.
public sealed class PNTimeScaleAreaVisualSettings
{
    public readonly float Lifetime, Speed, Size, Rate, Saturation, Chromatic, Vignette, Smoothness;
    public readonly int Maximum;
    public readonly float TintStrength, EdgeDistortion, Aspect, ParticleOpacity, RimIntensity, RimWidth, RimPulse, ContactWidth, ContactIntensity, BirthDuration, BirthIntensity, TwistStrength, TwistSpeed;
    private readonly float[] filter, vignetteColor, dustColor;
    public static JObject DefaultPayload() => JObject.Parse(@"{
      'particleLifetime':3,'particleSpeed':0.05,'particleSize':0.025,'particlesPerSecond':6,'maximumParticles':20,
      'colorFilter':[1.1607844,0,1.6549022],'saturation':-20,'chromaticAberration':0.12,
      'vignetteIntensity':0.15,'vignetteSmoothness':0.5,'vignetteColor':[0.60784316,0.34901962,1],
      'dustColor':[0.9,0.85,1],'tintStrength':0.58,'edgeDistortion':0.009,'particleAspect':3,'particleOpacity':0.3,'rimIntensity':0.12,'rimWidth':0.1,'rimPulse':0.12,'contactWidth':0.015,'contactIntensity':0,'birthDuration':0.35,'birthIntensity':0,'twistStrength':0.018,'twistSpeed':0.45}");
    public PNTimeScaleAreaVisualSettings(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token ?? DefaultPayload());
        PNBuffContentBatch.Fields(obj, "particleLifetime", "particleSpeed", "particleSize", "particlesPerSecond", "maximumParticles", "colorFilter", "saturation", "chromaticAberration", "vignetteIntensity", "vignetteSmoothness", "vignetteColor", "dustColor", "tintStrength", "edgeDistortion", "particleAspect", "particleOpacity", "rimIntensity", "rimWidth", "rimPulse", "contactWidth", "contactIntensity", "birthDuration", "birthIntensity", "twistStrength", "twistSpeed");
        float Number(string field, double min, double max)
        { double value = PNBuffContentBatch.Number(obj, field); if (value < min || value > max) throw new FormatException("Invalid area visual " + field); return (float)value; }
        float Optional(string field, double min, double max)
        { if(obj[field]==null) { var defaults=DefaultPayload(); double value=PNBuffContentBatch.Number(defaults,field); return (float)value; } return Number(field,min,max); }
        TintStrength=Optional("tintStrength",0,1); EdgeDistortion=Optional("edgeDistortion",0,.01);
        RimIntensity=Optional("rimIntensity",0,1); RimWidth=Optional("rimWidth",.01,.5); RimPulse=Optional("rimPulse",0,.5);
        ContactWidth=Optional("contactWidth",.001,.2); ContactIntensity=Optional("contactIntensity",0,1);
        BirthDuration=Optional("birthDuration",.01,1.5); BirthIntensity=Optional("birthIntensity",0,1);
        TwistStrength=Optional("twistStrength",0,.05); TwistSpeed=Optional("twistSpeed",0,2);
        Aspect=Optional("particleAspect",1,10); ParticleOpacity=Optional("particleOpacity",0,1);
        Lifetime = Number("particleLifetime", .001, 10000); Speed = Number("particleSpeed", 0, 10000);
        Size = Number("particleSize", .001, 10000); Rate = Number("particlesPerSecond", 0, 10000);
        Maximum = PNBuffContentBatch.Integer(obj, "maximumParticles"); if (Maximum < 1) throw new FormatException("Positive particle maximum required.");
        Saturation = Number("saturation", -100, 100); Chromatic = Number("chromaticAberration", 0, 1);
        Vignette = Number("vignetteIntensity", 0, 1); Smoothness = Number("vignetteSmoothness", .001, 1);
        float[] Color(string name)
        {
            if (!(obj[name] is JArray values) || values.Count != 3) throw new FormatException("RGB required: " + name);
            var result = new float[3];
            for (int i = 0; i < 3; i++)
            { var holder = new JObject { ["value"] = values[i].DeepClone() }; double value = PNBuffContentBatch.Number(holder, "value"); if (value < 0 || value > 16) throw new FormatException("Invalid HDR color: " + name); result[i] = (float)value; }
            return result;
        }
        filter = Color("colorFilter"); vignetteColor = Color("vignetteColor"); dustColor = Color("dustColor");
    }
#if UNITY_5_3_OR_NEWER
    public UnityEngine.Color Filter => new UnityEngine.Color(filter[0], filter[1], filter[2]).linear;
    public UnityEngine.Color VignetteColor => new UnityEngine.Color(vignetteColor[0], vignetteColor[1], vignetteColor[2]);
    public UnityEngine.Color DustColor => new UnityEngine.Color(dustColor[0], dustColor[1], dustColor[2]);
#endif
}
