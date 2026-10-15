using System;
using System.Linq;
using EveEngine;
using static EveSessionSmokeTests;

public static class PNUIDataTests
{
    public static void Run()
    {
        Reject(()=>PNAbilityDefinition.Parse("[{id:'test:a',kind:'State'}]"),"Ability UI flag is required");
        Reject(()=>PNAbilityDefinition.Parse("[{id:'test:a',kind:'State',showInUI:1}]"),"UI flag rejects numeric coercion");
        Reject(()=>PNAbilityDefinition.Parse("[{id:'test:a',kind:'State',showInUI:true,iconPath:1}]"),"Icon path must be a string");
        Reject(()=>PNAbilityDefinition.Parse("[{id:'test:a',kind:'State',showInUI:true,displayOrder:1.5}]"),"UI order must be an integer");
        foreach(var path in new[]{"Resources/Icon","Icon.png","../Icon","UI//Icon"," UI/Icon","C:/Icon","UI\\Icon"})
            Reject(()=>EveResourcePath.Validate(path),"Reject invalid resource path "+path);
        var character=new EveCharacterDefinition(EveId.Parse("test:portrait"),Array.Empty<EveInitialStat>(),null,"UI/Portrait/Eve");
        Check(character.PortraitPath=="UI/Portrait/Eve","Registry keeps Resources path without Sprite");
        EveRegistryHub Portrait(string field) => new EveContentBootstrap().Build(Array.Empty<EveIContentModule>(),
            new[]{new EveContentDocument("portrait.json","{schemaVersion:1,characters:[{id:'test:portrait'"+field+"}]}","test")});
        var portraitHub=Portrait(",portraitPath:'UI/Portrait/Eve'");
        Check(portraitHub.Characters.Get(character.Id).PortraitPath==character.PortraitPath,"Character parser preserves Resources portrait metadata");
        Check(Portrait("").Characters.Get(character.Id).PortraitPath=="","Omitted portrait preserves older schema compatibility");
        Reject(()=>Portrait(",portraitPath:5"),"Character parser rejects nonstring portrait");
        Reject(()=>Portrait(",portraitPath:'UI/Eve.png'"),"Character parser validates portrait path");
        var compatibility=EveContentCompatibility.Create(portraitHub,Array.Empty<EveContentDocument>(),"test:UI");
        Reject(()=>compatibility.Validate(EveContentCompatibility.Create(Portrait(",portraitPath:'UI/Portrait/Other'"),
            Array.Empty<EveContentDocument>(),"test:UI")),"Selected portrait metadata participates in content compatibility");
        var defs=PNAbilityDefinition.Parse("[{id:'test:z',kind:'Counter',showInUI:true,maximum:5},"+
            "{id:'test:a',kind:'State',showInUI:true,reapply:'Independent',duration:10,displayOrder:-1,iconPath:'UI/Icon/A'},"+
            "{id:'test:hidden',kind:'State',showInUI:false}]");
        using(var set=new PNAbilityCollection(defs))
        {
            int changes=0; set.Changed+=_=>changes++;
            var before=PNAbilityDisplay.Read(set);
            Check(before.Count==2 && before[0].Definition.Id==EveId.Parse("test:a"),"Filter hidden abilities and sort by registered order");
            Check(!before[0].IsAvailable && before[0].ActiveInstances==0,"Inactive Ability remains in layout");
            set.GrantState(EveId.Parse("test:a"),8); set.GrantState(EveId.Parse("test:a"),3);
            set.GetCounter(EveId.Parse("test:z")).Add(2);
            int events=changes; var rows=PNAbilityDisplay.Read(set);
            Check(rows[0].ActiveInstances==2 && rows[0].Remaining==3,"Independent states aggregate count and nearest expiry");
            Check(rows[1].Amount==2 && rows[1].Maximum==5 && rows[1].Progress==.4,"Counter read model uses live amount and capacity");
            Check(changes==events && set.GetStates(EveId.Parse("test:a"))[0].DurationRemaining==8,"Presentation never ticks or mutates abilities");
            Check(before[0].ActiveInstances==0,"Earlier display snapshot remains immutable");
            set.Define(new PNAbilitySettings { Id="test:permanent",Kind=PNAbilityKind.State,ShowInUI=true }.Create());
            set.GrantState(EveId.Parse("test:permanent"));
            Check(PNAbilityDisplay.Read(set).Single(r=>r.Definition.Id==EveId.Parse("test:permanent")).HasPermanentInstance,"Untimed state exposes permanent marker");
        }
    }
}
