using System;
using UnityEngine;

public class InteractiveObject : MonoBehaviour
{
    [SerializeField] protected String nameObject;
    
    protected virtual void OnEnable() { }

    protected virtual void OnDisable(){ }
    
    protected virtual void Start() { }

    protected virtual void Update(){ }
    

    protected virtual void OnTriggerEnter2D(Collider2D other) { }

    protected virtual void OnTriggerStay2D(Collider2D other) { }

    protected virtual void OnTriggerExit2D(Collider2D other) { }
}
