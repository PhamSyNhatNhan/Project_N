using System;
using System.Collections.Generic;

[Serializable]
public class RogueBuffSaveData
{
    [Serializable]
    public class GroupState
    {
        public BuffGroupId groupId;
        public bool[]      minorStates;  // length = 6, major tự unlock khi đủ count
    }

    public List<GroupState> groupStates = new List<GroupState>();
}