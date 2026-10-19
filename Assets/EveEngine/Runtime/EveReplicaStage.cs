using System;

namespace EveEngine
{
    // Staged replica state: prepared off the published view, committed with the entity batch, then published (or disposed on rollback).
    public interface EveIReplicaStage : IDisposable { void Commit(); void Published(); }
}
