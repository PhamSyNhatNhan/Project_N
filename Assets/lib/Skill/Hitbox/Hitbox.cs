using System.Collections.Generic;
using UnityEngine;

public class Hitbox : MonoBehaviour
{
    protected List<Color> hitboxColors = new List<Color>
    {
        Color.red,
        Color.green,
        Color.blue,
        Color.yellow,
        Color.magenta,
        Color.cyan,
        Color.grey,
        Color.black,
        Color.white,
        new Color(1.0f, 0.5f, 0.0f) 
    };
    
    public virtual List<GameObject> detectObject(LayerMask enableDamage)
    {
        return null;
    }
    public virtual List<GameObject> detectObject(LayerMask enableDamage, int hitBox)
    {
        return null;
    }
}
