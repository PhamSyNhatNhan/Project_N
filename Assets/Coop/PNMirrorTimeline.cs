using System;
using EveEngine;
using Mirror;
namespace ProjectN.Coop
{
    public sealed class PNMirrorTimeline : IPNNetworkTimeline
    {
        private readonly PNNetworkManager manager;private readonly EveSessionContext session;private readonly double delay;
        public PNMirrorTimeline(PNNetworkManager manager,EveSessionContext session,double additionalPresentationDelay=.1)
        {this.manager=manager??throw new ArgumentNullException(nameof(manager));this.session=session??throw new ArgumentNullException(nameof(session));PNTimeMath.Nonnegative(additionalPresentationDelay);delay=additionalPresentationDelay;}
        public PNTimeSample Sample()
        {
            bool authority=session.HasAuthority;
            Guid admitted=authority?session.SessionId:session.Admission.AdmittedHostSession;
            bool ready=session.IsActive&&manager.IsPlaying&&(authority||NetworkClient.isConnected&&manager.ClientInputReady&&admitted!=Guid.Empty);
            double local=NetworkTime.localTime;
            // Host-now is an estimate sampled at reconciliation, never a simulation delta.
            double estimate=authority?local:Math.Max(0,NetworkTime.predictedTime-NetworkTime.rtt/2);
            return new PNTimeSample(admitted==Guid.Empty?session.SessionId:admitted,ready,local,estimate,authority?local:Math.Max(0,NetworkTime.unscaledTime-delay),authority?0:Math.Max(0,NetworkTime.rtt/2));
        }
    }
}
