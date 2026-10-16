using UnityEngine;
// Independent from pause/menu ownership. A transport interruption cannot release a menu's input gate.
public sealed class PNNetworkInputGate : MonoBehaviour { public bool Blocked; }
