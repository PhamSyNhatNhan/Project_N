using System;
public sealed class PNGraceWindow
{
    public double Duration {get;}
    public double StartedAt {get;private set;}=-1;
    private double last;
    public PNGraceWindow(double duration)
    {if(double.IsNaN(duration)||double.IsInfinity(duration)||duration<=0)throw new ArgumentOutOfRangeException(nameof(duration));Duration=duration;}
    private void Observe(double now)
    {if(double.IsNaN(now)||double.IsInfinity(now)||now<last)throw new ArgumentOutOfRangeException(nameof(now));last=now;}
    public void Interrupt(double now){Observe(now);if(StartedAt<0)StartedAt=now;}
    public bool Expired(double now){Observe(now);return StartedAt>=0&&now-StartedAt>=Duration;}
    public bool TryResume(double now,bool activeSession,bool sameBinding,bool requiredStateSettled)
    {
        if(Expired(now)||StartedAt<0||!activeSession||!sameBinding||!requiredStateSettled)return false;
        StartedAt=-1;return true;
    }
}
