#if UNITY_EDITOR
using System;
using System.IO;
using Newtonsoft.Json.Linq;
public static class PNActionRecoveryChecks
{
    public static string Run(string folder)
    {
        int checks=0;void C(bool ok,string why){if(!ok)throw new Exception("T5.2-A: "+why);checks++;}
        var scope=PNActionC2Codec.MakeScope(1);
        using(var budget=new PNActionPublicationBudget())using(var census=new PNActionPublicationRecovery(budget))
        {
            PNActionPublication source=new PNActionPublication(sessionBudget:budget);
            for(int i=1;i<=64;i++){var k=new PNActionRunKey(1,i);census.Begin(k,"test:e","test:d","test:a",1,0,1);census.Advance(k,2,i*.125,8);}
            source.Dispose();var old=source;
            C(census.Recover(ref source,2,5)==PNActionRecoveryState.Recovered,"complete64 rebuild");
            C(source.Source!=old.Source&&old.Disposed&&source.ActiveCount==64,"new source and full set");
            using(var lease=source.Capture(scope,2))
            {var rows=(JArray)JArray.Parse(lease.Json)[5];C(rows.Count==64,"complete baseline count");for(int i=0;i<64;i++){C((long)rows[i][0][1]==i+1,"identity");C((double)rows[i][1][6]==1&&(double)rows[i][4]==(i+1)*.125&&(double)rows[i][5]==8,"original start/context/progress/rate");}}
            C(source.Sequence==0&&source.JournalBytes==0,"no fabricated past");
            source.Advance(new PNActionRunKey(1,1),2,3,.5,1);census.Advance(new PNActionRunKey(1,1),3,.5,1);
            source.Dispose();census.End(new PNActionRunKey(1,2));census.Seal(3);
            C(census.Recover(ref source,3,6)==PNActionRecoveryState.Recovered&&source.ActiveCount==63,"end during outage");
            C(source.Progress(new PNActionRunKey(1,1))==.625,"progress continued");source.Dispose();
            for(int i=1;i<=64;i++)census.End(new PNActionRunKey(1,i));
            C(census.Recover(ref source,4,7)==PNActionRecoveryState.Recovered&&source.ActiveCount==0,"verified empty");source.Dispose();
        }
        using(var budget=new PNActionPublicationBudget(800))using(var census=new PNActionPublicationRecovery(budget))
        {
            census.Begin(new PNActionRunKey(2,1),"test:e","test:d","test:a",1,0,1);
            // Competing active owner leaves enough for census but not a rebuild.
            var competitor=new PNActionPublication(sessionBudget:budget);competitor.Begin(new PNActionRunKey(3,1),"test:e","test:d","test:a",1,0,1);
            PNActionPublication source=null;int charge=budget.Used;
            C(census.Recover(ref source,1,3)==PNActionRecoveryState.Pending&&source==null,"bounded staging refuses");
            C(budget.Used==charge,"candidate rollback");
            C(census.Recover(ref source,2,99)==PNActionRecoveryState.Pending&&census.Deadline==3,"retry cannot extend deadline");
            competitor.Dispose();C(census.Recover(ref source,2.5,99)==PNActionRecoveryState.Recovered,"resources returned then rebuild");source.Dispose();
        }
        using(var budget=new PNActionPublicationBudget())using(var census=new PNActionPublicationRecovery(budget))
        {
            PNActionPublication source=null;census.Begin(new PNActionRunKey(1,1),"test:e","test:d","test:a",1,0,1);census.Invalidate();
            C(census.Recover(ref source,1,3)==PNActionRecoveryState.Failed&&source==null,"incomplete census never publishes partial");
            C(census.Recover(ref source,2,99)==PNActionRecoveryState.Failed,"sticky failed episode");
        }
        using(var budget=new PNActionPublicationBudget())using(var census=new PNActionPublicationRecovery(budget))
        {PNActionPublication source=null;C(census.Recover(ref source,3,3)==PNActionRecoveryState.Failed,"deadline expires on empty queue");C(source==null,"no late success");}
        using(var budget=new PNActionPublicationBudget())
        {var census=new PNActionPublicationRecovery(budget);census.Begin(new PNActionRunKey(1,1),"test:e","test:d","test:a",1,0,1);budget.Dispose();C(budget.Used==0&&census.Count==0,"session disposes census");}
        using(var budget=new PNActionPublicationBudget())using(var census=new PNActionPublicationRecovery(budget,1))
        {PNActionPublication source=null;census.Begin(new PNActionRunKey(1,1),"test:e","test:d","test:a",1,0,1);C(!census.Complete&&census.Recover(ref source,1,3)==PNActionRecoveryState.Failed,"census bound cannot silently omit");}
        for(int cycle=0;cycle<100;cycle++)using(var budget=new PNActionPublicationBudget())
        {var census=new PNActionPublicationRecovery(budget);census.Begin(new PNActionRunKey(1,1),"test:e","test:d","test:a",1,0,1);PNActionPublication source=null;C(census.Recover(ref source,1,4)==PNActionRecoveryState.Recovered,"scope rebuild cycle");budget.Dispose();C(budget.Used==0&&source.Disposed&&census.Count==0,"scope release cycle");}
        var text="PASS "+checks+" T5.2-A recovery checks";Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"result.txt"),text);return text;
    }
}
#endif
