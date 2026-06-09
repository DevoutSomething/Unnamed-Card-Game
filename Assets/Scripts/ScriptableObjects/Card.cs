using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Cards/CardDefinition")]
public class CardDefinition : ScriptableObject {
    public string Id;
    public string DisplayName;
    public int Cost;
    public CardType Type;           
    public int BaseAttack;
    public int BaseHealth;

    public List<string> Abilities = new();   
}


public enum CardType {
    Guy,
    Spell,
}