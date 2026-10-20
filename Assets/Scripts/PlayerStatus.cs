using UnityEngine;

// Points de vie, sérénité et inventaire (5 cases) du joueur local.
public class PlayerStatus : MonoBehaviour
{
    public int Hp { get; private set; } = GameRules.MaxHp;
    public int Serenity { get; private set; } = GameRules.MaxSerenity;
    public bool Dead { get; private set; }
    // Note 10 (V0.4.3) : une case est toujours sélectionnée, la case 1 par défaut.
    public int Selected { get; private set; }

    readonly ItemType[] slots = new ItemType[GameRules.InventorySize];

    public event System.Action Changed;

    public ItemType this[int slot] => slots[slot];

    public int ItemCount
    {
        get
        {
            int n = 0;
            foreach (var s in slots) if (s != ItemType.None) n++;
            return n;
        }
    }

    public int FreeSlots => GameRules.InventorySize - ItemCount;

    public int Add(ItemType type)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] == ItemType.None)
            {
                slots[i] = type;
                Changed?.Invoke();
                return i;
            }
        return -1;
    }

    public ItemType RemoveAt(int slot)
    {
        ItemType t = slots[slot];
        slots[slot] = ItemType.None;
        Changed?.Invoke();
        return t;
    }

    public void ClearInventory()
    {
        for (int i = 0; i < slots.Length; i++) slots[i] = ItemType.None;
        Selected = 0;
        Changed?.Invoke();
    }

    // Touche 1 à 5 : sélectionne la case (V0.4.3 : on ne peut plus la désélectionner).
    public void Select(int slot)
    {
        if (Selected == slot) return;
        Selected = slot;
        Changed?.Invoke();
    }

    public void SetHp(int hp)
    {
        hp = Mathf.Clamp(hp, 0, GameRules.MaxHp);
        if (hp == Hp) return;
        Hp = hp;
        Changed?.Invoke();
    }

    public void SetSerenity(int value)
    {
        value = Mathf.Clamp(value, 0, GameRules.MaxSerenity);
        if (value == Serenity) return;
        Serenity = value;
        Changed?.Invoke();
    }

    public int IndexOf(ItemType type)
    {
        for (int i = 0; i < slots.Length; i++) if (slots[i] == type) return i;
        return -1;
    }

    public void SetDead(bool dead)
    {
        Dead = dead;
        if (dead) Hp = 0;
        Changed?.Invoke();
    }
}
