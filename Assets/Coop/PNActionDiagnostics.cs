using System;
namespace ProjectN.Coop
{
    // Opt-in verification seam. With no subscriber the delivery path allocates no
    // diagnostic objects. The fixture owns and clears these hooks on shutdown.
    public static class PNActionDiagnostics
    {
        public static Func<int,bool> BlockSend;
        public static event Action<string,int,PNActionPacket?,PNActionReceiver,double,double> Sample;
        public static bool Enabled => Sample != null;
        internal static long ObserverTicks {get;private set;}
        public static long CallbackTicks {get;private set;}
        public static long CallbackCount {get;private set;}
        public static long CoreInclusiveTicks {get;private set;}
        public static long CoreExclusiveTicks {get;private set;}
        internal static void RecordCore(long inclusive,long observer){CoreInclusiveTicks+=inclusive;CoreExclusiveTicks+=inclusive-observer;}
        internal static void Emit(string kind,int peer,PNActionPacket? packet,PNActionReceiver receiver,double now,double value)
        {
            var sample=Sample;if(sample==null)return;
            long begin=System.Diagnostics.Stopwatch.GetTimestamp();
            try{sample(kind,peer,packet,receiver,now,value);}
            finally{long elapsed=System.Diagnostics.Stopwatch.GetTimestamp()-begin;ObserverTicks+=elapsed;CallbackTicks+=elapsed;CallbackCount++;}
        }
    }
}
