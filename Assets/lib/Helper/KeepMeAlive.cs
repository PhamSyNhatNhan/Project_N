using UnityEngine;
using System.Collections.Generic; 

public class KeepMeAlive : MonoBehaviour
{
    public string uniqueID; 

    private static Dictionary<string, KeepMeAlive> instances = new Dictionary<string, KeepMeAlive>();

    /*
    void Awake()
    {
        if (!instances.ContainsKey(uniqueID))
        {
            instances.Add(uniqueID, this);
            DontDestroyOnLoad(this.gameObject);
        }
        else
        {
            if (instances[uniqueID] != this)
            {
                Destroy(this.gameObject);
            }
        }
    }
    */
}